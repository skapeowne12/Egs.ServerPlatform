# EGS File Manager And Mod Task Design

This is a design note for the later file manager, modpack, and tool-install phase. It is intentionally not implemented yet.

## Scope

The browser must never directly access a node filesystem and must not receive a raw host shell by default. Website actions create audited cloud or local commands, and the agent validates and executes those commands against configured roots.

Initial workflows to support later:

- View and edit allowed server configuration files.
- Upload and extract modpacks.
- Install mod loaders such as BepInEx for Valheim.
- Install Minecraft server packs such as Technic-style packs.
- Run approved maintenance tasks such as SteamCMD update, validate files, backup, and restore.

## Roles

- Viewer: view status and delayed console.
- ServerAdmin: start, stop, restart, send game server console input, edit configs, upload mods into allowed server folders.
- NodeOwner or SuperAdmin: manage bootstrap/deploy tokens, install mod managers or node tools, run approved node tasks, and perform advanced file operations.
- AdvancedShell: disabled by default, audited, and only available later to NodeOwner or SuperAdmin.

## Command Families

Game console commands are distinct from node tasks:

- `ServerConsoleCommand`: sends text to the running game server process stdin. It never executes as an OS shell command.
- `NodeTask` or `ToolTask`: runs approved maintenance/install tasks from server-side definitions.

Future command types:

- `FileList`
- `FileRead`
- `FileWrite`
- `FileUpload`
- `FileDownload`
- `FileDelete`
- `FileRename`
- `FileCreateDirectory`
- `ArchiveExtract`
- `InstallModPack`
- `InstallModLoader`
- `InstallTool`
- `RunModManager`
- `ValidateServerFiles`
- `BackupServer`
- `RestoreBackup`
- `ServerConsoleCommand`
- `NodeTask`

## Path Rules

All paths supplied by the website must be relative to an allowed root. The agent must reject:

- absolute paths
- `..` path traversal
- symlinks or junctions escaping the allowed root
- blocked extensions unless explicitly allowed for a SuperAdmin task
- files over configured size limits

Example agent configuration:

```json
{
  "FileManager": {
    "Enabled": true,
    "MaxUploadMb": 500,
    "AllowedRoots": [
      {
        "Key": "servers",
        "Path": "C:\\Egs\\Servers",
        "AllowWrite": true
      },
      {
        "Key": "steamcmd",
        "Path": "C:\\Egs\\SteamCMD",
        "AllowWrite": false
      },
      {
        "Key": "tools",
        "Path": "C:\\Egs\\Tools",
        "AllowWrite": true
      }
    ]
  }
}
```

## Uploads

Large uploads should be staged through Azure Blob Storage or a website-owned upload store. The command payload should contain a file id or blob URL, expected size, and checksum. The agent downloads the staged file, verifies the checksum, optionally backs up the target, then extracts or installs it.

## Tool Definitions

Do not hardcode every mod manager into core agent code. Prefer per-game or per-tool definitions with:

- `toolKey`
- `displayName`
- `supportedGameKeys`
- `installSteps`
- `executablePath`
- `argumentsTemplate`
- `workingDirectory`
- `allowedExtensions`
- `outputParsing`

This keeps Technic packs, Valheim BepInEx installs, SteamCMD validation, and future tools extensible.

## Security TODOs

- Add audit rows for every write/install/delete task.
- Add admin role checks before enabling file manager UI.
- Add per-command approval policies for NodeOwner/SuperAdmin tasks.
- Keep `AdvancedNodeCommand` disabled by default if it is ever introduced.
