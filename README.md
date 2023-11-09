<div  align=center>
    <img src="https://github.com/FerdinandSukhoi/RCloneDriver/blob/main/images/Full_2048.png?raw=true" width = 30% height = 30%  />
</div>

# RCloneDriver

![GitHub](https://img.shields.io/github/license/FerdinandSukhoi/RCloneDriver?style=flat-square)
![GitHub last commit](https://img.shields.io/github/last-commit/FerdinandSukhoi/RCloneDriver?style=flat-square)
![GitHub Workflow Status](https://img.shields.io/github/workflow/status/FerdinandSukhoi/RCloneDriver/publish_to_nuget?style=flat-square)
![GitHub repo size](https://img.shields.io/github/repo-size/FerdinandSukhoi/RCloneDriver?style=flat-square)
![GitHub code size](https://img.shields.io/github/languages/code-size/FerdinandSukhoi/RCloneDriver?style=flat-square)

A wrapped driver for RClone, supporting syncronization following the config files in `./.rcd/`.

## How to use

### 1. Before you start

Install [rclone](https://github.com/rclone/rclone), configure your RClone configs, set up remotes.

### 2. Initialize the local repository

At a local directory, run `rcd init <Remote>`, remote is the remote directory, e.g. `netdisk:/rcdbackup`

### 3. Customize the configuration at `./.rcd/conf.json`

Like this:

```json
{
    "Remote": "netdisk:/rcdtest",
    "Timestamp": "2023-11-05T21:20:47.4325265+08:00",
    "UpdateStrategy": 0,
    "TrackRenames": false,
    "Excludes": [
        {
            "Type": 0,
            "Pattern": "- *.exe"
        },
        {
            "Type": 1,
            "Pattern": ".rcd/.rcdexcluding"
        },
        {
            "Type": 2,
            "Pattern": ".rcdignore"
        }
    ]
}
```

You might need to customize it.

#### `TrackRenames` Property

Use `--track-renames` option or not. **Notice the option only works when checksum is available.**

#### `UpdateStrategy` Property

```csharp
public enum UpdateStrategy
{
    Checksum = 1, // Use --checksum
    ModTime = 0, // Use --update option
    SizeOnly = -1 // Use --size-only option
}
```

#### `Excludes` Property

It is defined as `FilterOption[]` where:

```csharp
public record FilterOption(FilterOptionType Type, string Pattern);
public enum FilterOptionType
{
    FilesMatchingPattern = 0, // Filter files matching pattern
    ReadFileFilterPatternsFromFile = 1, // Filter file filter patterns from file
    DirectoriesIfFilenamePresented = 2, // Exclude directories if filename is present
}
```

**Please refer to the [`rclone` official documentation](https://rclone.org/commands/rclone_sync/) for more details.**

### 4. Run Syncronization by running `rcd` at the root of your repository

### 5. Clone a existing repository

To start (clone repository like `git clone`) from another place, just run `rcd clone <remote>[ <local-dir-name>]`,
where `local-dir-name` is optional, by default it will be `remote.Split("/")[^1]`.

**Of course, you can also use `rclone copy` or other `cp` commands manually.**

## Tools

Win32 `.reg` files is included in the `/tools` directory, which can install `rcd` command to your windows content menu.

**Notice: This `.reg` uses `pwsh`(Powershell Core) as shell, which can be changed as `powershell` (Windows Powershell) alternatively if you don't have it installed. ** However, Powershell Core is much more newer and better than Windows Powershell, you can get it from Windows Store.

### Push and Pull repository

You can specify the synchronization direction manually. `rcd push` and `rcd pull` follow the same direction habit as `git push` and `git pull`.

## How it works

### Initializing

1. Create a `.rcd` directory, write default configuration into `.rcd/conf.json`
2. Run `rclone copy .rcd <remote>/.rcd`

### Syncronizing

1. Change current directory if the argument passed
2. Pull remote config & status into `./.rcd-remote`: `rclone copy <remote>/.rcd ./.rcd-remote`
3. Compare timestamp between local and remote ones
4. Choose the latest one as `src` and another as `des`
5. Merge & update configs:
   1. Merge config, update timestamp
   2. Remove `./.rcd-remote` or `./.rcd-old` (if remote config newer)
6. Run `rclone sync [src] [des] <update-strategy> -P --create-empty-src-dirs <track-renames> <filter-options> --filter "+ .rcd" --dry-run`
7. Based on the decision os the user, Continue/Interactive/Quit
   1. Continue: `rclone sync [src] [des] <update-strategy> -P --create-empty-src-dirs <track-renames> <filter-options> --filter "+ .rcd"`
   2. Interactive: `rclone sync [src] [des] <update-strategy> -P --create-empty-src-dirs <track-renames> <filter-options> --filter "+ .rcd" -i`
   3. Quit: Just `return 0`

### Pulling

1. Backup local config to `./.rcd-old`
2. Pull remote config & status into `./.rcd`: `rclone copy <remote>/.rcd ./.rcd`
3. Run `rclone sync <remote> . -i <update-strategy> -P --create-empty-src-dirs <track-renames> <filter-options> --filter "+ .rcd" --dry-run`
4. Based on the decision os the user, Continue/Interactive/Quit
   1. Continue: `rclone sync <remote> . <update-strategy> -P --create-empty-src-dirs <track-renames> <filter-options> --filter "+ .rcd"`
   2. Interactive: `rclone sync <remote> . <update-strategy> -P --create-empty-src-dirs <track-renames> <filter-options> --filter "+ .rcd" -i`
   3. Quit: Just `return 0`

### Pushing

1. Run `rclone sync <remote> . -i <update-strategy> -P --create-empty-src-dirs <track-renames> <filter-options> --filter "+ .rcd" --dry-run`
2. Based on the decision os the user, Continue/Interactive/Quit
   1. Continue: `rclone sync . <remote> <update-strategy> -P --create-empty-src-dirs <track-renames> <filter-options> --filter "+ .rcd"`
   2. Interactive: `rclone sync . <remote> <update-strategy> -P --create-empty-src-dirs <track-renames> <filter-options> --filter "+ .rcd" -i`
   3. Quit: Just `return 0`

### Cloning

1. Decide local directory
2. Run `rclone copy <remote> <local>`

## Troubleshooting

### Repo not found or broken

Happens when the directory `.rcd` does not exist or `.rcd-remote` already exists. Initialize repository or remove existing `.rcd-remote`.

### Failed to Read/Write Local/Remote Config

Happens when the `rcd` program fails to read or write `./.rcd/conf.json` or `./.rcd-remote/conf.json`. Maybe the file does not exist or being used by another process.

### Failed to Update Local Config

Failed to delete `./.rcd-remote` or `./.rcd-old` directory. Please check why, delete it manually and re-run `rcd`.

## Known Limitations

1. This tool is not parallel secure and is still in development, which may lead to unexpected accidents.
2. Before and after working with local files, you need to run `rcd`
3. rclone use `hash` to trach renames, so after you rename/modify the same file, you should do sync first before do another operation.
4. WebDAV does not support large file.
5. rclone `--exclude-if-present` option does not work with directories, so if you need to exclude Git repositories, use `--exclude-if-present .gitignore` and remember to add `.gitignore` to all your repositories (That's a good habit).
6. Sync shall start when local timestamp is newer than or equal to the remote timestamp.