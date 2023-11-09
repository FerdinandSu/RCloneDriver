using System.Diagnostics;
using System.Text.Json;
using IFers.RCloneDriver;
using Microsoft.Extensions.Logging;

return args switch
{
    ["init", _] => await Init(args[1]),
    ["clone", ..] => await Clone(args[1..]),
    ["push"] => await Push(),
    ["pull"] => await Pull(),
    [_] => await Sync(args[0]),
    [] => await Sync(),
    _ => -1
};

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

void Log(LogLevel level, string format, params object[] args)
{
    Console.WriteLine($"[{level}][{DateTime.Now:HH:mm:ss}]{format}", args);
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

async Task<int> RunSync(RcdConfig conf, string src, string des)
{
    Log(LogLevel.Information, "Calling RClone; {0} => {1}.", src, des);
    var @params = new[]
        {
            "sync",
            src,
            des,
            conf.UpdateStrategy switch
            {
                UpdateStrategy.Checksum => "--checksum",
                UpdateStrategy.ModTime => "--update",
                UpdateStrategy.SizeOnly => "--size-only",
                _ => throw new ArgumentOutOfRangeException()
            },
            "-P",
            "--create-empty-src-dirs",
            conf.TrackRenames ? "--track-renames" : ""
        }.Concat(conf.Excludes.SelectMany(ex => ex.ToStringArray()))
        .Concat(new[]
        {
            "--filter",
            "\"+ .rcd/\""
        })
        .ToArray();
    await RClone(@params.Append("--dry-run").ToArray());
    await Console.Out.WriteLineAsync(
        "Dry run completed, type C/i/q to [Continue(default)],[Use Interactive], or [Quit(other characters)].");
    var cmd = Console.ReadLine();
    switch (cmd)
    {
        case "c" or "":
            await RClone(@params);
            break;
        case "i":
            await RClone(@params.Append("--interactive").ToArray());
            ;
            break;
    }

    return 0;
}

async Task<int> Sync(string dir = ".")
{
    Directory.SetCurrentDirectory(dir);
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
    return await RunSync(latest, src, des);
}

async Task<int> Pull()
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

    try
    {
        Directory.Move(".rcd", ".rcd-old");
    }
    catch (Exception)
    {
        Log(LogLevel.Critical, "Failed to Update Local Config.");
        return 1;
    }

    await RClone(new[]
    {
        "copy",
        $"{localConfig.Remote}/.rcd",
        ".rcd"
    });
    var remoteConfig = await ReadConfig(".rcd/conf.json");
    if (remoteConfig == null)
    {
        Log(LogLevel.Critical, "Failed to Read Remote Config.");
        return 1;
    }

    Log(LogLevel.Information, "Got Remote Config.");

    return await RunSync(remoteConfig, remoteConfig.Remote, ".");
}

async Task<int> Push()
{
    if (!Directory.Exists(".rcd"))
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

    return await RunSync(localConfig, ".", localConfig.Remote);
}

async Task<int> Init(string remote)
{
    if (Directory.Exists(".rcd"))
    {
        Log(LogLevel.Error, "Repo already exists.");
        return 1;
    }

    Directory.CreateDirectory(".rcd");
    var conf = new RcdConfig(remote, DateTime.Now, UpdateStrategy.ModTime, false, new List<FilterOption>
    {
        new(FilterOptionType.DirectoriesIfFilenamePresented, ".gitignore")
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