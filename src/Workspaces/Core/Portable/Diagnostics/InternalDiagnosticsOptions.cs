﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CodeAnalysis.Options;

namespace Microsoft.CodeAnalysis.Diagnostics
{
    internal static class InternalDiagnosticsOptions
    {
        private const string LocalRegistryPath = @"Roslyn\Internal\Diagnostics\";

        public static readonly Option2<bool> PreferLiveErrorsOnOpenedFiles = new(nameof(InternalDiagnosticsOptions), "Live errors will be preferred over errors from build on opened files from same analyzer", defaultValue: true,
            storageLocation: new LocalUserProfileStorageLocation(LocalRegistryPath + "Live errors will be preferred over errors from build on opened files from same analyzer"));

        public static readonly Option2<bool> PreferBuildErrorsOverLiveErrors = new(nameof(InternalDiagnosticsOptions), "Errors from build will be preferred over live errors from same analyzer", defaultValue: true,
            storageLocation: new LocalUserProfileStorageLocation(LocalRegistryPath + "Errors from build will be preferred over live errors from same analyzer"));

        public static readonly Option2<bool> CrashOnAnalyzerException = new(nameof(InternalDiagnosticsOptions), nameof(CrashOnAnalyzerException), defaultValue: false,
            storageLocation: new LocalUserProfileStorageLocation(LocalRegistryPath + "CrashOnAnalyzerException"));

        public static readonly Option2<bool> ProcessHiddenDiagnostics = new(nameof(InternalDiagnosticsOptions), nameof(ProcessHiddenDiagnostics), defaultValue: false,
            storageLocation: new LocalUserProfileStorageLocation(LocalRegistryPath + "Process Hidden Diagnostics"));

        public static readonly Option2<DiagnosticMode> NormalDiagnosticMode = new(nameof(InternalDiagnosticsOptions), nameof(NormalDiagnosticMode), defaultValue: DiagnosticMode.Default,
            storageLocation: new LocalUserProfileStorageLocation(LocalRegistryPath + "NormalDiagnosticMode"));

        public static readonly Option2<bool> EnableFileLoggingForDiagnostics = new(nameof(InternalDiagnosticsOptions), nameof(EnableFileLoggingForDiagnostics), defaultValue: false,
            storageLocation: new LocalUserProfileStorageLocation(LocalRegistryPath + "EnableFileLoggingForDiagnostics"));
    }
}
