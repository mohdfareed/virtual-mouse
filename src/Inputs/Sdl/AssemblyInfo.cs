using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: InternalsVisibleTo("Hosting")]
[assembly: InternalsVisibleTo("Cli")]
[assembly: InternalsVisibleTo("Inputs.Tests")]
[assembly: DefaultDllImportSearchPaths(
    DllImportSearchPath.AssemblyDirectory |
    DllImportSearchPath.ApplicationDirectory |
    DllImportSearchPath.UserDirectories |
    DllImportSearchPath.SafeDirectories)]
