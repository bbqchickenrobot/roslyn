﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeGeneration;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.PickMembers;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.GenerateOverrides
{
    internal partial class GenerateOverridesCodeRefactoringProvider
    {
        private class GenerateOverridesWithDialogCodeAction : CodeActionWithOptions
        {
            private readonly GenerateOverridesCodeRefactoringProvider _service;
            private readonly Document _document;
            private readonly INamedTypeSymbol _containingType;
            private readonly ImmutableArray<ISymbol> _viableMembers;
            private readonly TextSpan _textSpan;

            public GenerateOverridesWithDialogCodeAction(
                GenerateOverridesCodeRefactoringProvider service,
                Document document,
                TextSpan textSpan,
                INamedTypeSymbol containingType,
                ImmutableArray<ISymbol> viableMembers)
            {
                _service = service;
                _document = document;
                _containingType = containingType;
                _viableMembers = viableMembers;
                _textSpan = textSpan;
            }

            public override string Title => FeaturesResources.Generate_overrides;

            public override object GetOptions(CancellationToken cancellationToken)
            {
                var service = _service._pickMembersService_forTestingPurposes ?? _document.Project.Solution.Workspace.Services.GetRequiredService<IPickMembersService>();
                return service.PickMembers(
                    FeaturesResources.Pick_members_to_override,
                    _viableMembers,
                    selectAll: _document.Project.Solution.Options.GetOption(GenerateOverridesOptions.SelectAll));
            }

            protected override async Task<IEnumerable<CodeActionOperation>> ComputeOperationsAsync(object options, CancellationToken cancellationToken)
            {
                var result = (PickMembersResult)options;
                if (result.IsCanceled || result.Members.Length == 0)
                    return SpecializedCollections.EmptyEnumerable<CodeActionOperation>();

                var syntaxTree = await _document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false);
                RoslynDebug.AssertNotNull(syntaxTree);

                // If the user has selected just one member then we will insert it at the current
                // location.  Otherwise, if it's many members, then we'll auto insert them as appropriate.
                var afterThisLocation = result.Members.Length == 1
                    ? syntaxTree.GetLocation(_textSpan)
                    : null;

                var generator = SyntaxGenerator.GetGenerator(_document);
                var memberTasks = result.Members.SelectAsArray(
                    m => GenerateOverrideAsync(generator, m, cancellationToken));

                var members = await Task.WhenAll(memberTasks).ConfigureAwait(false);

                var newDocument = await CodeGenerator.AddMemberDeclarationsAsync(
                    _document.Project.Solution,
                    _containingType,
                    members,
                    new CodeGenerationContext(
                        afterThisLocation: afterThisLocation,
                        contextLocation: syntaxTree.GetLocation(_textSpan)),
                    cancellationToken).ConfigureAwait(false);

                return new CodeActionOperation[]
                    {
                        new ApplyChangesOperation(newDocument.Project.Solution),
                        new ChangeOptionValueOperation(result.SelectedAll),
                    };
            }

            private Task<ISymbol> GenerateOverrideAsync(
                SyntaxGenerator generator, ISymbol symbol,
                CancellationToken cancellationToken)
            {
                return generator.OverrideAsync(
                    symbol, _containingType, _document,
                    cancellationToken: cancellationToken);
            }

            private class ChangeOptionValueOperation : CodeActionOperation
            {
                private readonly bool _selectedAll;

                public ChangeOptionValueOperation(bool selectedAll)
                    => _selectedAll = selectedAll;

                public override void Apply(Workspace workspace, CancellationToken cancellationToken)
                {
                    if (workspace.Options.GetOption(GenerateOverridesOptions.SelectAll) == _selectedAll)
                        return;

                    workspace.TryApplyChanges(workspace.CurrentSolution.WithOptions(
                        workspace.CurrentSolution.Options.WithChangedOption(
                            GenerateOverridesOptions.SelectAll, _selectedAll)));
                }
            }
        }
    }
}
