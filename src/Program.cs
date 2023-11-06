﻿using System.Diagnostics;
using System.Text.Json;
using IFers.RCloneDriver;
using Microsoft.Extensions.Logging;

async Task RClone(string[] args)
{
    var proc = new Process();
    proc.StartInfo = new ProcessStartInfo
    {
        FileName = "rclone",
        Arguments = string.Join(" ", args)
    };

    Log(LogLevel.Trace, $"rclone {string.Join(" ", args)}");

    proc.Start();
    await proc.WaitForExitAsync();
}

async Task<RcdConfig?> ReadConfig(string path)
{
    try
    {
        var txt = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<RcdConfig>(txt);
    }
    catch (Exception)
    {
        return null;
    }
}

async Task<bool> WriteConfig(string path, RcdConfig config)
{
    try
    {
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(config));
        return true;
    }
    catch (Exception)
    {
        return false;
    }
}

void Log(LogLevel level, string format, params object[] args)
{
    Console.WriteLine($"[{level}][{DateTime.Now:HH:mm:ss}]{format}", args);
}

(RcdConfig, int) MergeConfig(RcdConfig config1, RcdConfig config2)
{
    var (src, des) = (config1, config2);
    if (src.Timestamp == des.Timestamp) return (config1, 0);
    if (src.Timestamp < des.Timestamp) (src, des) = (des, src);
    return (src with
    {
        Timestamp = DateTime.Now
    }, src == config1 ? 1 : -1); // if config1 is latest, return 1, else -1
}

async Task<int> Clone(string[] xargs)
{
    var remote = xargs[0];
    var local = xargs.Length > 1 ? xargs[1] : remote.Split("/")[^1];
    if (Directory.Exists(local))
    {
        Log(LogLevel.Error, "Repo already exists.");
        return 1;
    }

    await RClone(new[]
    {
        "copy",
        remote,
        local
    });
    return 0;
}

async Task<int> Init(string remote)
{
    if (Directory.Exists(".rcd"))
    {
        Log(LogLevel.Error, "Repo already exists.");
        return 1;
    }

    Directory.CreateDirectory(".rcd");
    var conf = new RcdConfig(remote, DateTime.Now, new List<FilterOption>
    {
        new FilterOption(FilterOptionType.FilesMatchingPattern, "node_modules/")
    });
    await WriteConfig(".rcd/conf.json", conf);
    await RClone(new[]
    {
        "copy",
        "./.rcd",
        $"{conf.Remote}/.rcd"
    });
    return 0;
}

async Task<int> Sync()
{
    if (!Directory.Exists(".rcd") || Directory.Exists(".rcd-remote"))
    {
        Log(LogLevel.Critical, "Repo not found or broken.");
        return 1;
    }

    var localConfig = await ReadConfig(".rcd/conf.json");
    if (localConfig == null)
    {
        Log(LogLevel.Critical, "Failed to Read Local Config.");
        return 1;
    }

    await RClone(new[]
    {
        "copy",
        $"{localConfig.Remote}/.rcd",
        ".rcd-remote"
    });
    var remoteConfig = await ReadConfig(".rcd-remote/conf.json");
    if (remoteConfig == null)
    {
        Log(LogLevel.Critical, "Failed to Read Remote Config.");
        return 1;
    }

    Log(LogLevel.Information, "Got Remote Config.");
    var (latest, cmp) = MergeConfig(localConfig, remoteConfig);


    if (cmp < 0)
        try
        {
            Directory.Move(".rcd", ".rcd-old");
            Directory.Move(".rcd-remote", ".rcd");
        }
        catch (Exception)
        {
            Log(LogLevel.Critical, "Failed to Update Local Config.");
            return 1;
        }

    if (!await WriteConfig(".rcd/conf.json", latest))
    {
        Log(LogLevel.Critical, "Failed to Write Local Config.");
        return 1;
    }

    try
    {
        Directory.Delete(cmp < 0 ? ".rcd-old" : ".rcd-remote", true);
    }
    catch (Exception)
    {
        Log(LogLevel.Critical, "Failed to Update Local Config.");
        return 1;
    }

    Log(LogLevel.Information, "Config updated.");
    var (src, des) = cmp >= 0 ? (".", latest.Remote) : (latest.Remote, ".");
    Log(LogLevel.Information, "Calling RClone; {0} => {1}.", src, des);
    var @params = new[]
        {
            "sync",
            src,
            des,
            "-c",
            "-P",
            "--create-empty-src-dirs",
            "--track-renames"
        }.Concat(latest.Excludes.SelectMany(ex => ex.ToStringArray()))
        .Concat(new[]
        {
            "--filter",
            "\"+ .rcd/\""
        })
        .ToArray();
    await RClone(args.Append("--dry-run").ToArray());
    await Console.Out.WriteLineAsync(
        "Dry run completed, type C/i/q to [Continue(default)],[Use Interactive], or [Quit(other characters)].");
    var cmd = Console.ReadLine();
    if (cmd is "c" or "")
    {
        await RClone(args);
    }
    else if (cmd is "i")
    {
        await RClone(args.Append("--interactive").ToArray());
        ;
    }

    return 0;
}

return args switch
{
    ["init", _] => await Init(args[1]),
    ["clone", ..] => await Clone(args[1..]),
    [] => await Sync(),
    _ => -1
};