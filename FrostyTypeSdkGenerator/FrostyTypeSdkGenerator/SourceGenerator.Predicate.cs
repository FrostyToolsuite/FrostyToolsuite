﻿using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace FrostyTypeSdkGenerator;

public sealed partial class SourceGenerator
{
    private static bool StructPredicate(SyntaxNode node, CancellationToken cancellationToken)
    {
        return node is StructDeclarationSyntax { Members.Count: > 0 } structDeclarationSyntax && structDeclarationSyntax.Members.Any(m => m is not TypeDeclarationSyntax and not ConstructorDeclarationSyntax) && structDeclarationSyntax.Modifiers.Any(SyntaxKind.PartialKeyword);
    }

    private static bool EmptyStructPredicate(SyntaxNode node, CancellationToken cancellationToken)
    {
        return node is StructDeclarationSyntax structDeclarationSyntax && structDeclarationSyntax.Modifiers.Any(SyntaxKind.PartialKeyword) && (structDeclarationSyntax.Members.Count == 0 || !structDeclarationSyntax.Members.Any(m => m is FieldDeclarationSyntax));
    }

    private static bool BaseClassPredicate(SyntaxNode node, CancellationToken cancellationToken)
    {
        return node is ClassDeclarationSyntax { BaseList: null } classDeclarationSyntax && classDeclarationSyntax.Modifiers.Any(SyntaxKind.PartialKeyword) && !classDeclarationSyntax.Modifiers.Any(SyntaxKind.StaticKeyword) && classDeclarationSyntax.AttributeLists.Count > 0;
    }

    private static bool DataContainerPredicate(SyntaxNode node, CancellationToken cancellationToken)
    {
        return node is ClassDeclarationSyntax { BaseList: not null } classDeclarationSyntax && classDeclarationSyntax.BaseList.Types.Any(static type => (type.Type as QualifiedNameSyntax)?.Right.Identifier.Text == "DataContainer");

    }

    private static bool NonDataContainerPredicate(SyntaxNode node, CancellationToken cancellationToken)
    {
        return node is ClassDeclarationSyntax { BaseList: not null } classDeclarationSyntax && classDeclarationSyntax.BaseList.Types.Any(static type => (type.Type as QualifiedNameSyntax)?.Right.Identifier.Text != "DataContainer") && classDeclarationSyntax.Members.Any(static member => (member as FieldDeclarationSyntax)?.Declaration.Variables.Any(v => v.Identifier.Text == "_Name") == true);
    }

    private static bool TypePredicate(SyntaxNode node, CancellationToken cancellationToken)
    {
        return node is TypeDeclarationSyntax { Members.Count: > 0 } typeDeclarationSyntax && typeDeclarationSyntax.Modifiers.Any(SyntaxKind.PartialKeyword) && !typeDeclarationSyntax.Modifiers.Any(SyntaxKind.StaticKeyword) && typeDeclarationSyntax.Members.Any(static member => member is FieldDeclarationSyntax);
    }
}
