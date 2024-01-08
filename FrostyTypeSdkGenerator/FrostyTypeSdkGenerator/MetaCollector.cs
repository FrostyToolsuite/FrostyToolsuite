using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace FrostyTypeSdkGenerator;

public class MetaCollector : CSharpSyntaxWalker
{
    public readonly Dictionary<string, Dictionary<string, MetaProperty>> Meta = new();

    public override void VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        VisitStructOrClassDeclaration(node);
    }

    public override void VisitStructDeclaration(StructDeclarationSyntax node)
    {
        VisitStructOrClassDeclaration(node);
    }

    private void VisitStructOrClassDeclaration(TypeDeclarationSyntax node)
    {
        string name = node.Identifier.Text;

        // just expect there to be properties
        if (!Meta.ContainsKey(name))
        {
            Meta.Add(name, new Dictionary<string, MetaProperty>());
        }

        foreach (MemberDeclarationSyntax member in node.Members)
        {
            if (member is PropertyDeclarationSyntax property)
            {
                MetaProperty meta = new();

                string content = property.GetText().ToString();

                foreach (AttributeListSyntax attributeList in property.AttributeLists)
                {
                    foreach (AttributeSyntax attribute in attributeList.Attributes)
                    {
                        if (attribute.Name.ToString() == "OverrideAttribute")
                        {
                            meta.IsOverride = true;
                            content = content.Replace(attributeList.GetText().ToString(), string.Empty);
                        }
                        else if (attribute.Name.ToString() == "DependsOnAttribute")
                        {
                            meta.DependsOnProperty = attribute.ArgumentList?.Arguments[0].GetText().ToString() ?? string.Empty;
                            content = content.Replace(attributeList.GetText().ToString(), string.Empty);
                        }
                    }
                }

                meta.Content = content;

                Meta[name].Add(property.Identifier.Text, meta);
            }
        }
    }
}