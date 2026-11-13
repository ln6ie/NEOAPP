# NEO third-party dependencies

Production code uses Microsoft .NET 10 and Windows Desktop APIs, NAudio 2.2.1 (MIT) for WASAPI, Markdig 1.6.0 (BSD-2-Clause) for Markdown parsing, and PdfPig 0.1.16 (Apache-2.0) for PDF text extraction. Retain restored dependency license and notice files when distributing. .NET source is MIT licensed, subject to its distribution notices; Windows components remain subject to Microsoft's applicable terms. The self-contained Windows publish includes the .NET runtime.

Tests use the .NET template's Microsoft.NET.Test.Sdk (MIT), xUnit (Apache-2.0), xunit.runner.visualstudio (MIT), and coverlet.collector (MIT). Check restored package metadata and notices before redistributing test artifacts.

llama.cpp is an external service, not bundled; its source is MIT licensed. Model weights and their licensing are separate and are never included in NEO. The project owner's NEO license selection is pending.
