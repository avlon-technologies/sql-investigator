# Third-party notices

SQL Investigator is licensed under the MIT License (see [`LICENSE`](LICENSE)). It uses the third-party components
below, each under its own license. The source repository contains none of them: `dotnet restore` downloads them from
NuGet. The release archives (`SqlInvestigator-<version>-<rid>.zip` / `.tar.gz`) are self-contained, so they
redistribute these components and the .NET runtime in binary form, together with this file and the license texts in
`licenses/`.

## Microsoft.Data.SqlClient.SNI (Windows release only)

`Microsoft.Data.SqlClient.SNI.dll`, from the `Microsoft.Data.SqlClient.SNI.runtime` package (7.1.0), is **not** open
source. It is redistributed under the **Microsoft Software License Terms for the Microsoft.Data.SqlClient.SNI
Library**. The full text is in `licenses/Microsoft.Data.SqlClient.SNI.LICENSE.txt`.

**By using the Windows release, you also agree to those terms for that component.** Among other things, they prohibit
reverse engineering it, redistributing it as a standalone file, and removing Microsoft's notices from it. The macOS
builds do not include this component.

## Apache License 2.0

The full text is in `licenses/Apache-2.0.txt`.

| Component | Version | Project |
|---|---|---|
| ModelContextProtocol | 2.3.1 | https://github.com/modelcontextprotocol/csharp-sdk |
| ModelContextProtocol.Core | 2.3.1 | https://github.com/modelcontextprotocol/csharp-sdk |

## MIT License

Each component below is Copyright (c) Microsoft Corporation and licensed under the MIT License, with the same terms as
[`LICENSE`](LICENSE) apart from the copyright line.

| Component | Version | Project |
|---|---|---|
| .NET runtime and base class libraries (bundled by self-contained publishing) | 10.0 | https://github.com/dotnet/runtime |
| Microsoft.Data.SqlClient, .Extensions.Abstractions, .Internal.Logging | 7.1.1 | https://github.com/dotnet/SqlClient |
| Microsoft.SqlServer.Server | 1.0.0 | https://github.com/dotnet/SqlClient |
| Microsoft.SqlServer.TransactSql.ScriptDom | 180.140.0 | https://github.com/microsoft/SqlScriptDOM |
| Microsoft.Extensions.* (Hosting, Configuration, DependencyInjection, Logging, Options, Caching, FileProviders, Diagnostics, Primitives, FileSystemGlobbing) | 9.0.18 – 10.0.14 | https://github.com/dotnet/runtime |
| Microsoft.Extensions.AI.Abstractions | 10.11.0 | https://github.com/dotnet/extensions |
| Microsoft.Bcl.Cryptography, System.Configuration.ConfigurationManager, System.Diagnostics.EventLog, System.Security.Cryptography.Pkcs, System.Security.Cryptography.ProtectedData, System.Threading.RateLimiting | 9.0.18 – 10.0.14 | https://github.com/dotnet/runtime |
| Microsoft.IdentityModel.* (Abstractions, JsonWebTokens, Logging, Protocols, Protocols.OpenIdConnect, Tokens), System.IdentityModel.Tokens.Jwt | 8.16.0 | https://github.com/AzureAD/azure-activedirectory-identitymodel-extensions-for-dotnet |

## Development-only components (not redistributed)

These are used to build and test SQL Investigator and are not in the release archives: xUnit (Apache-2.0),
xunit.runner.visualstudio (Apache-2.0), Microsoft.NET.Test.Sdk (MIT), and Microsoft SQL Server container images (used
under Microsoft's own license terms, which you accept with `ACCEPT_EULA=Y`).

## Trademarks

SQL Server, Windows and .NET are trademarks of Microsoft Corporation. Claude and Claude Code are trademarks of
Anthropic, PBC. macOS is a trademark of Apple Inc. SQL Investigator is an independent project of Avlon Technologies. It
is not affiliated with, endorsed by or sponsored by any of these companies.

Keep this file current when `Directory.Packages.props` changes.
