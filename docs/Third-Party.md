# Third-party dependency review

Direct application package versions:

| Package | Version | Declared license / purpose |
|---|---|---|
| NAPS2.Sdk | 1.6.0 | LGPL-2.1-or-later; acquisition and PDF export |
| NAPS2.Images.Wpf | 1.6.0 | NAPS2 image backend; verify exact package license metadata |
| NAPS2.Sdk.Worker.Win32 | 1.6.0 | Win32 bridge; verify exact package license metadata |
| NAPS2.Pdfium.Binaries | 1.3.0 | Native PDF decode for eSCL PDF transport; bundled third-party notices |
| PdfPig | 0.1.11 | Apache-2.0; output PDF validation |
| Microsoft.Extensions.DependencyInjection | 10.0.15 | MIT; service construction |
| Microsoft.Extensions.Logging | 10.0.15 | MIT; logging abstractions and provider |
| WPF-UI | 4.3.0 | MIT; Fluent control styles, window title bar and icon font for the application UI |

Tests also use Microsoft.NET.Test.Sdk 17.14.1, xUnit 2.9.3, xUnit VS runner 2.8.2. Test tools are not deliberately included in application distributions.

NAPS2 Desktop GPL licensing must not be substituted for the SDK's licensing. No NAPS2 desktop CLI is invoked. SDK dependencies, including its PDF, TWAIN, image and font components, need their own notices.

`build/collect_licenses.py` records actual restored package versions, NuGet-declared licenses, bundled notice files and repository commits. Build packages include those records. LGPL components remain separate replaceable libraries. Exact corresponding-source archives are collected where NuGet identifies a GitHub commit. Confirm all LGPL components have appropriate corresponding source and notices before external redistribution; do not assume a project URL alone is sufficient.

Official references:
- https://www.naps2.com/sdk/doc/api/
- https://github.com/cyanfish/naps2/blob/master/NAPS2.Sdk/LICENSE
- https://github.com/cyanfish/naps2/blob/master/NAPS2.Sdk.Worker.Build/NAPS2.Sdk.Worker.Build.csproj
- https://learn.microsoft.com/dotnet/core/install/windows
- https://github.com/UglyToad/PdfPig

Native PDFium 152.0.7947.0 notices are checked in under `build/license-overrides/Pdfium-chromium-7947`. The upstream x64 binary was compared byte-for-byte to the NuGet-shipped binary. Makaretu.Dns 2.0.1 lacks license metadata in its NuGet package, so its MIT license is included from repository commit 701463d2091e6d98d4cc4490abb0e0ead8ae2985.
