﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.ExternalAccess.UnitTesting.Api;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.StackTraceExplorer;

namespace Microsoft.CodeAnalysis.ExternalAccess.UnitTesting
{
    internal class UnitTestingStackTraceServiceAccessor : IUnitTestingStackTraceServiceAccessor
    {
        private readonly IStackTraceExplorerService _stackTraceExplorerService;

        [Obsolete(MefConstruction.FactoryMethodMessage, error: true)]
        public UnitTestingStackTraceServiceAccessor(
            IStackTraceExplorerService stackTraceExplorerService)
        {
            _stackTraceExplorerService = stackTraceExplorerService;
        }

        public (Document? document, int lineNumber) GetDocumentAndLine(Workspace workspace, UnitTestingParsedFrameWrapper parsedFrame)
            => _stackTraceExplorerService.GetDocumentAndLine(workspace.CurrentSolution, parsedFrame.UnderlyingObject);

        public async Task<UnitTestingDefinitionItemWrapper?> TryFindMethodDefinitionAsync(Workspace workspace, UnitTestingParsedFrameWrapper parsedFrame, CancellationToken cancellationToken)
        {
            var definition = await _stackTraceExplorerService.TryFindDefinitionAsync(workspace.CurrentSolution, parsedFrame.UnderlyingObject, StackFrameSymbolPart.Method, cancellationToken).ConfigureAwait(false);
            return definition is null
                ? null
                : new UnitTestingDefinitionItemWrapper(definition);
        }

        public async Task<ImmutableArray<UnitTestingParsedFrameWrapper>> TryParseAsync(string input, Workspace workspace, CancellationToken cancellationToken)
        {
            var result = await StackTraceAnalyzer.AnalyzeAsync(input, cancellationToken).ConfigureAwait(false);
            return result.ParsedFrames.SelectAsArray(p => new UnitTestingParsedFrameWrapper(p));
        }

        public Task<bool> TryNavigateToAsync(Workspace workspace, UnitTestingDefinitionItemWrapper definitionItem, bool showInPreviewTab, bool activateTab, CancellationToken cancellationToken)
            => definitionItem.UnderlyingObject.TryNavigateToAsync(workspace, showInPreviewTab, activateTab, cancellationToken);
    }
}
