﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis.Differencing;
using Microsoft.CodeAnalysis.EditAndContinue.Contracts;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.PooledObjects;
using Microsoft.CodeAnalysis.Test.Utilities;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Test.Utilities;
using Roslyn.Utilities;
using Xunit;
using static Microsoft.CodeAnalysis.EditAndContinue.AbstractEditAndContinueAnalyzer;

namespace Microsoft.CodeAnalysis.EditAndContinue.UnitTests
{
    internal abstract class EditAndContinueTestHelpers
    {
        public const EditAndContinueCapabilities BaselineCapabilities = EditAndContinueCapabilities.Baseline;

        public const EditAndContinueCapabilities Net5RuntimeCapabilities =
            EditAndContinueCapabilities.Baseline |
            EditAndContinueCapabilities.AddInstanceFieldToExistingType |
            EditAndContinueCapabilities.AddStaticFieldToExistingType |
            EditAndContinueCapabilities.AddMethodToExistingType |
            EditAndContinueCapabilities.NewTypeDefinition;

        public const EditAndContinueCapabilities Net6RuntimeCapabilities =
            Net5RuntimeCapabilities |
            EditAndContinueCapabilities.ChangeCustomAttributes |
            EditAndContinueCapabilities.UpdateParameters;

        public abstract AbstractEditAndContinueAnalyzer Analyzer { get; }
        public abstract SyntaxNode FindNode(SyntaxNode root, TextSpan span);
        public abstract ImmutableArray<SyntaxNode> GetDeclarators(ISymbol method);
        public abstract string LanguageName { get; }
        public abstract TreeComparer<SyntaxNode> TopSyntaxComparer { get; }

        private void VerifyDocumentActiveStatementsAndExceptionRegions(
            ActiveStatementsDescription description,
            SyntaxTree oldTree,
            SyntaxTree newTree,
            ImmutableArray<ActiveStatement> actualNewActiveStatements,
            ImmutableArray<ImmutableArray<SourceFileSpan>> actualNewExceptionRegions)
        {
            // check active statements:
            AssertSpansEqual(description.NewMappedSpans, actualNewActiveStatements.OrderBy(x => x.Ordinal).Select(s => s.FileSpan), newTree);

            var oldRoot = oldTree.GetRoot();

            // check old exception regions:
            foreach (var oldStatement in description.OldStatements)
            {
                var oldRegions = Analyzer.GetExceptionRegions(
                    oldRoot,
                    oldStatement.UnmappedSpan,
                    isNonLeaf: oldStatement.Statement.IsNonLeaf,
                    CancellationToken.None);

                AssertSpansEqual(oldStatement.ExceptionRegions.Spans, oldRegions.Spans, oldTree);
            }

            // check new exception regions:
            if (!actualNewExceptionRegions.IsDefault)
            {
                Assert.Equal(actualNewActiveStatements.Length, actualNewExceptionRegions.Length);
                Assert.Equal(description.NewMappedRegions.Length, actualNewExceptionRegions.Length);
                for (var i = 0; i < actualNewActiveStatements.Length; i++)
                {
                    var activeStatement = actualNewActiveStatements[i];
                    AssertSpansEqual(description.NewMappedRegions[activeStatement.Ordinal], actualNewExceptionRegions[i], newTree);
                }
            }
        }

        internal void VerifyLineEdits(
            EditScript<SyntaxNode> editScript,
            SequencePointUpdates[] expectedLineEdits,
            SemanticEditDescription[]? expectedSemanticEdits,
            RudeEditDiagnosticDescription[]? expectedDiagnostics)
        {
            VerifySemantics(
                new[] { editScript },
                TargetFramework.NetStandard20,
                new[] { new DocumentAnalysisResultsDescription(semanticEdits: expectedSemanticEdits, lineEdits: expectedLineEdits, diagnostics: expectedDiagnostics) },
                capabilities: Net5RuntimeCapabilities);
        }

        internal void VerifySemantics(EditScript<SyntaxNode>[] editScripts, TargetFramework targetFramework, DocumentAnalysisResultsDescription[] expectedResults, EditAndContinueCapabilities? capabilities = null)
        {
            Assert.True(editScripts.Length == expectedResults.Length);
            var documentCount = expectedResults.Length;

            using var workspace = new AdhocWorkspace(FeaturesTestCompositions.Features.GetHostServices());
            CreateProjects(editScripts, workspace, targetFramework, out var oldProject, out var newProject);

            var oldDocuments = oldProject.Documents.ToArray();
            var newDocuments = newProject.Documents.ToArray();

            Debug.Assert(oldDocuments.Length == newDocuments.Length);

            var oldTrees = oldDocuments.Select(d => d.GetSyntaxTreeSynchronously(default)!).ToArray();
            var newTrees = newDocuments.Select(d => d.GetSyntaxTreeSynchronously(default)!).ToArray();

            var testAccessor = Analyzer.GetTestAccessor();
            var allEdits = new List<SemanticEditInfo>();
            var lazyCapabilities = AsyncLazy.Create(capabilities ?? Net5RuntimeCapabilities);

            for (var documentIndex = 0; documentIndex < documentCount; documentIndex++)
            {
                var assertMessagePrefix = (documentCount > 0) ? $"Document #{documentIndex}" : null;

                var expectedResult = expectedResults[documentIndex];

                var includeFirstLineInDiagnostics = expectedResult.Diagnostics.Any(d => d.FirstLine != null) == true;
                var newActiveStatementSpans = expectedResult.ActiveStatements.OldUnmappedTrackingSpans;

                // we need to rebuild the edit script, so that it operates on nodes associated with the same syntax trees backing the documents:
                var oldTree = oldTrees[documentIndex];
                var newTree = newTrees[documentIndex];
                var oldRoot = oldTree.GetRoot();
                var newRoot = newTree.GetRoot();

                var oldDocument = oldDocuments[documentIndex];
                var newDocument = newDocuments[documentIndex];

                var oldModel = oldDocument.GetSemanticModelAsync().Result;
                var newModel = newDocument.GetSemanticModelAsync().Result;
                Contract.ThrowIfNull(oldModel);
                Contract.ThrowIfNull(newModel);

                var lazyOldActiveStatementMap = AsyncLazy.Create(expectedResult.ActiveStatements.OldStatementsMap);
                var result = Analyzer.AnalyzeDocumentAsync(oldProject, lazyOldActiveStatementMap, newDocument, newActiveStatementSpans, lazyCapabilities, CancellationToken.None).Result;
                var oldText = oldDocument.GetTextSynchronously(default);
                var newText = newDocument.GetTextSynchronously(default);

                VerifyDiagnostics(expectedResult.Diagnostics, result.RudeEditErrors.ToDescription(newText, includeFirstLineInDiagnostics), assertMessagePrefix);

                if (!expectedResult.SemanticEdits.IsDefault)
                {
                    if (result.HasChanges)
                    {
                        VerifySemanticEdits(expectedResult.SemanticEdits, result.SemanticEdits, oldModel.Compilation, newModel.Compilation, oldRoot, newRoot, assertMessagePrefix);

                        allEdits.AddRange(result.SemanticEdits);
                    }
                    else
                    {
                        Assert.True(expectedResult.SemanticEdits.IsEmpty);
                        Assert.True(result.SemanticEdits.IsDefault);
                    }
                }

                if (!result.HasChanges)
                {
                    Assert.True(result.ExceptionRegions.IsDefault);
                    Assert.True(result.ActiveStatements.IsDefault);
                }
                else
                {
                    // exception regions not available in presence of rude edits:
                    Assert.Equal(!expectedResult.Diagnostics.IsEmpty, result.ExceptionRegions.IsDefault);

                    VerifyDocumentActiveStatementsAndExceptionRegions(
                        expectedResult.ActiveStatements,
                        oldTree,
                        newTree,
                        result.ActiveStatements,
                        result.ExceptionRegions);
                }

                if (!result.RudeEditErrors.IsEmpty)
                {
                    Assert.True(result.LineEdits.IsDefault);
                    Assert.True(expectedResult.LineEdits.IsDefaultOrEmpty);
                }
                else if (!expectedResult.LineEdits.IsDefault)
                {
                    // check files of line edits:
                    AssertEx.Equal(
                        expectedResult.LineEdits.Select(e => e.FileName),
                        result.LineEdits.Select(e => e.FileName),
                        itemSeparator: ",\r\n",
                        message: "File names of line edits differ in " + assertMessagePrefix);

                    // check lines of line edits:
                    _ = expectedResult.LineEdits.Zip(result.LineEdits, (expected, actual) =>
                    {
                        AssertEx.Equal(
                            expected.LineUpdates,
                            actual.LineUpdates,
                            itemSeparator: ",\r\n",
                            itemInspector: s => $"new({s.OldLine}, {s.NewLine})",
                            message: "Line deltas differ in " + assertMessagePrefix);

                        return true;
                    }).ToArray();
                }
            }

            var duplicateNonPartial = allEdits
                .Where(e => e.PartialType == null)
                .GroupBy(e => e.Symbol, SymbolKey.GetComparer(ignoreCase: false, ignoreAssemblyKeys: true))
                .Where(g => g.Count() > 1)
                .Select(g => g.Key);

            AssertEx.Empty(duplicateNonPartial, "Duplicate non-partial symbols");

            // check if we can merge edits without throwing:
            EditSession.MergePartialEdits(oldProject.GetCompilationAsync().Result!, newProject.GetCompilationAsync().Result!, allEdits, out var _, out var _, CancellationToken.None);
        }

        public static void VerifyDiagnostics(IEnumerable<RudeEditDiagnosticDescription> expected, IEnumerable<RudeEditDiagnostic> actual, SourceText newSource)
            => VerifyDiagnostics(expected, actual.ToDescription(newSource, expected.Any(d => d.FirstLine != null)));

        public static void VerifyDiagnostics(IEnumerable<RudeEditDiagnosticDescription> expected, IEnumerable<RudeEditDiagnosticDescription> actual, string? message = null)
            => AssertEx.SetEqual(expected, actual, message: message, itemSeparator: ",\r\n");

        private void VerifySemanticEdits(
            ImmutableArray<SemanticEditDescription> expectedSemanticEdits,
            ImmutableArray<SemanticEditInfo> actualSemanticEdits,
            Compilation oldCompilation,
            Compilation newCompilation,
            SyntaxNode oldRoot,
            SyntaxNode newRoot,
            string? message = null)
        {
            // string comparison to simplify understanding why a test failed:
            AssertEx.Equal(
                expectedSemanticEdits.Select(e => $"{e.Kind}: {e.SymbolProvider(newCompilation)}"),
                actualSemanticEdits.NullToEmpty().Select(e => $"{e.Kind}: {e.Symbol.Resolve(newCompilation).Symbol}"),
                message: message);

            for (var i = 0; i < actualSemanticEdits.Length; i++)
            {
                var expectedSemanticEdit = expectedSemanticEdits[i];
                var actualSemanticEdit = actualSemanticEdits[i];
                var editKind = expectedSemanticEdit.Kind;

                Assert.Equal(editKind, actualSemanticEdit.Kind);

                var expectedOldSymbol = (editKind == SemanticEditKind.Update) ? expectedSemanticEdit.SymbolProvider(oldCompilation) : null;
                var expectedNewSymbol = expectedSemanticEdit.SymbolProvider(newCompilation);
                var symbolKey = actualSemanticEdit.Symbol;

                if (editKind == SemanticEditKind.Update)
                {
                    Assert.Equal(expectedOldSymbol, symbolKey.Resolve(oldCompilation, ignoreAssemblyKey: true).Symbol);
                    Assert.Equal(expectedNewSymbol, symbolKey.Resolve(newCompilation, ignoreAssemblyKey: true).Symbol);
                }
                else if (editKind is SemanticEditKind.Insert or SemanticEditKind.Replace)
                {
                    Assert.Equal(expectedNewSymbol, symbolKey.Resolve(newCompilation, ignoreAssemblyKey: true).Symbol);
                }
                else
                {
                    Assert.False(true, "Only Update, Insert or Replace allowed");
                }

                // Partial types must match:
                Assert.Equal(
                    expectedSemanticEdit.PartialType?.Invoke(newCompilation),
                    actualSemanticEdit.PartialType?.Resolve(newCompilation, ignoreAssemblyKey: true).Symbol);

                // Edit is expected to have a syntax map:
                var actualSyntaxMap = actualSemanticEdit.SyntaxMap;
                Assert.Equal(expectedSemanticEdit.HasSyntaxMap, actualSyntaxMap != null);

                // If expected map is specified validate its mappings with the actual one:
                var expectedSyntaxMap = expectedSemanticEdit.SyntaxMap;

                if (expectedSyntaxMap != null)
                {
                    Contract.ThrowIfNull(actualSyntaxMap);
                    VerifySyntaxMap(oldRoot, newRoot, expectedSyntaxMap, actualSyntaxMap);
                }
            }
        }

        private void VerifySyntaxMap(
            SyntaxNode oldRoot,
            SyntaxNode newRoot,
            IEnumerable<KeyValuePair<TextSpan, TextSpan>> expectedSyntaxMap,
            Func<SyntaxNode, SyntaxNode?> actualSyntaxMap)
        {
            foreach (var expectedSpanMapping in expectedSyntaxMap)
            {
                var newNode = FindNode(newRoot, expectedSpanMapping.Value);
                var expectedOldNode = FindNode(oldRoot, expectedSpanMapping.Key);
                var actualOldNode = actualSyntaxMap(newNode);

                Assert.Equal(expectedOldNode, actualOldNode);
            }
        }

        private void CreateProjects(EditScript<SyntaxNode>[] editScripts, AdhocWorkspace workspace, TargetFramework targetFramework, out Project oldProject, out Project newProject)
        {
            oldProject = workspace.AddProject("project", LanguageName).WithMetadataReferences(TargetFrameworkUtil.GetReferences(targetFramework));
            var documentIndex = 0;
            foreach (var editScript in editScripts)
            {
                oldProject = oldProject.AddDocument(documentIndex.ToString(), editScript.Match.OldRoot).Project;
                documentIndex++;
            }

            var newSolution = oldProject.Solution;
            documentIndex = 0;
            foreach (var oldDocument in oldProject.Documents)
            {
                newSolution = newSolution.WithDocumentSyntaxRoot(oldDocument.Id, editScripts[documentIndex].Match.NewRoot, PreservationMode.PreserveIdentity);
                documentIndex++;
            }

            newProject = newSolution.Projects.Single();
        }

        private static void AssertSpansEqual(IEnumerable<SourceFileSpan> expected, IEnumerable<SourceFileSpan> actual, SyntaxTree newTree)
        {
            AssertEx.Equal(
                expected,
                actual,
                itemSeparator: "\r\n",
                itemInspector: span => DisplaySpan(newTree, span));
        }

        private static string DisplaySpan(SyntaxTree tree, SourceFileSpan span)
        {
            if (tree.FilePath != span.Path)
            {
                return span.ToString();
            }

            var text = tree.GetText();
            var code = text.GetSubText(text.Lines.GetTextSpan(span.Span)).ToString().Replace("\r\n", " ");
            return $"{span}: [{code}]";
        }

        internal static IEnumerable<KeyValuePair<SyntaxNode, SyntaxNode>> GetMethodMatches(AbstractEditAndContinueAnalyzer analyzer, Match<SyntaxNode> bodyMatch)
        {
            Dictionary<SyntaxNode, LambdaInfo>? lazyActiveOrMatchedLambdas = null;
            var map = analyzer.GetTestAccessor().ComputeMap(bodyMatch, new ArrayBuilder<ActiveNode>(), ref lazyActiveOrMatchedLambdas, new ArrayBuilder<RudeEditDiagnostic>());

            var result = new Dictionary<SyntaxNode, SyntaxNode>();
            foreach (var pair in map.Forward)
            {
                if (pair.Value == bodyMatch.NewRoot)
                {
                    Assert.Same(pair.Key, bodyMatch.OldRoot);
                    continue;
                }

                result.Add(pair.Key, pair.Value);
            }

            return result;
        }

        public static MatchingPairs ToMatchingPairs(Match<SyntaxNode> match)
            => ToMatchingPairs(match.Matches.Where(partners => partners.Key != match.OldRoot));

        public static MatchingPairs ToMatchingPairs(IEnumerable<KeyValuePair<SyntaxNode, SyntaxNode>> matches)
        {
            return new MatchingPairs(matches
                .OrderBy(partners => partners.Key.GetLocation().SourceSpan.Start)
                .ThenByDescending(partners => partners.Key.Span.Length)
                .Select(partners => new MatchingPair
                {
                    Old = partners.Key.ToString().Replace("\r\n", " ").Replace("\n", " "),
                    New = partners.Value.ToString().Replace("\r\n", " ").Replace("\n", " ")
                }));
        }
    }

    internal static class EditScriptTestUtils
    {
        public static void VerifyEdits<TNode>(this EditScript<TNode> actual, params string[] expected)
            => AssertEx.Equal(expected, actual.Edits.Select(e => e.GetDebuggerDisplay()), itemSeparator: ",\r\n", itemInspector: s => $"\"{s}\"");
    }
}
