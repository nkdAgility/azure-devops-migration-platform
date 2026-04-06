# VS Code Launch Configurations for Azure DevOps Migration Platform

This directory contains VS Code launch configurations to easily run and test the CLI commands during development.

## Available Launch Configurations

### Migration CLI (`devopsmigration`)

**Configuration Commands:**
- 🔧 **Configure** - Interactive configuration wizard
- 🔧 **Configure with Output** - Create config file with specific output path

**Discovery Commands:**
- 🔍 **Discovery Inventory (Basic)** - Basic inventory using config file
- 🔍 **Discovery Inventory (All Projects)** - Inventory all projects using config file
- 🔍 **Discovery Inventory (Direct Credentials)** - Use direct URL and token parameters
- 🔍 **Legacy Inventory** - Run the backward-compatible legacy inventory command

**Export Commands:**
- 📦 **TFS Export** - Export work items from on-premises TFS/Azure DevOps Server

**Management Commands:**
- 📋 **Logs (Job ID)** - Retrieve logs for a specific job ID
- 📋 **Logs (Follow)** - Follow/tail logs for a running job

### TFS Migration CLI (`tfsmigration`)

**TFS-Specific Commands:**
- 🔴 **TFS Export** - Export work items from TFS collection
- 🔴 **TFS Inventory (All Projects)** - Count all work items across projects
- 🔴 **TFS Inventory (Single Project)** - Count work items for specific project

## Usage

1. **Open the Run and Debug view** in VS Code (Ctrl+Shift+D)
2. **Select a configuration** from the dropdown at the top
3. **Customize arguments** by editing the launch.json file if needed
4. **Press F5** to run the selected configuration

## Customizing Launch Configurations

### To add a new command:

1. **Add to launch.json** - Copy an existing configuration and modify:
   ```json
   {
       "name": "🆕 New Command Name",
       "type": "coreclr",
       "request": "launch",
       "program": "${workspaceFolder}/src/DevOpsMigrationPlatform.CLI.Migration/bin/Debug/net10.0/devopsmigration.dll",
       "args": [
           "your-command",
           "--your-option",
           "value"
       ],
       "cwd": "${workspaceFolder}",
       "console": "integratedTerminal",
       "stopAtEntry": false,
       "preLaunchTask": "build-migration-cli"
   }
   ```

2. **Use appropriate icons for grouping:**
   - 🔧 Configuration/Setup commands
   - 🔍 Discovery/Analysis commands  
   - 📦 Export/Import commands
   - 📋 Management/Logs commands
   - 🔴 TFS-specific commands
   - 🆕 New/Experimental commands

### To modify existing configurations:

1. **Edit arguments** - Update the `args` array with different parameters
2. **Change working directory** - Modify `cwd` if needed
3. **Add environment variables** - Add `env` section:
   ```json
   "env": {
       "AZDEVOPS_SYSTEM_TEST_ORG": "https://dev.azure.com/yourorg",
       "AZDEVOPS_SYSTEM_TEST_PAT": "your-token"
   }
   ```

## Build Tasks

The configurations automatically build the required projects before running. Available tasks:

- **build-migration-cli** - Builds only the Migration CLI project
- **build-tfs-cli** - Builds only the TFS Migration CLI project  
- **build-all** - Builds the entire solution
- **clean** - Cleans all build outputs

## Tips

- **System Tests**: For system test configurations, set environment variables in your system or add them to the `env` section of the launch configuration
- **Config Files**: Create test configuration files (e.g., `test-migration.json`) in the workspace root for easier testing
- **Breakpoints**: Set breakpoints in your code and use `stopAtEntry: true` to pause at the start of execution
- **Output Directories**: Use relative paths like `./test-output` to avoid cluttering your workspace

## Maintenance

When adding new CLI commands to either project:

1. **Update the respective Program.cs** to register the command
2. **Add launch configurations** following the naming convention
3. **Update this README** with the new command description
4. **Group similar commands** together in the launch.json file
5. **Use descriptive icons** for easy identification in the dropdown

This approach ensures the launch configurations stay synchronized with the actual CLI command structure as the application grows.