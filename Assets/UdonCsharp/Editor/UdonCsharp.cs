using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Extensions;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using VRC.Udon.Editor;
using VRC.Udon.Graph;

public class UdonCsharp : CSharpSyntaxWalker {
    public class Error : Exception {
        public Error(string message) : base(message) { }
    }

    public class TypeName {
        static Dictionary<string, string> Primitives = new Dictionary<string, string> {
            { "bool", "Boolean" },
            { "byte", "Byte" },
            { "sbyte", "SByte" },
            { "char", "Char" },
            { "decimal", "Decimal" },
            { "double", "Double" },
            { "float", "Single" },
            { "int", "Int32" },
            { "uint", "UInt32" },
            { "long", "Int64" },
            { "ulong", "UInt64" },
            { "object", "Object" },
            { "short", "Int16" },
            { "ushort", "Uint16" },
            { "string", "String" },
        };

        string Name { get; }

        public TypeName(string name) {
            Name = name;
        }

        public string FullName { get => Name.Contains(".") ? Name.Replace(".", "") : $"System{Primitives[Name]}"; }

        public string ShortName { get => Name.Split('.').Last(); }

        public string VariableName { get => $"Variable_{FullName}"; }
    }

    public static bool IsPrintDiagnostics = false;

    static Dictionary<string, UdonNodeDefinition> UdonNodeDefinitions {
        get {
            return UdonNodeDefinitionsCache ?? (
                UdonNodeDefinitionsCache =
                    UdonEditorManager.Instance.GetNodeDefinitions().ToDictionary(d => d.fullName, d => d)
                );
        }
    }

    static Dictionary<string, UdonNodeDefinition> UdonNodeDefinitionsCache;

    public string Code { get; }
    public UdonGraph UdonGraph { get; }

    Microsoft.CodeAnalysis.SyntaxTree SyntaxTree;
    SemanticModel SemanticModel;

    public UdonCsharp(string code) {
        Code = code;
        UdonGraph = ScriptableObject.CreateInstance<UdonGraph>();
        if (UdonGraph.data == null) UdonGraph.data = new UdonGraphData();
        if (UdonGraph.graphProgramAsset == null) UdonGraph.graphProgramAsset = ScriptableObject.CreateInstance<UdonGraphProgramAsset>();
    }

    // Start is called before the first frame update
    public void Compile() {
        GetTree();
        Visit(SyntaxTree.GetCompilationUnitRoot());
    }

    void GetTree() {
        // パース
        SyntaxTree = CSharpSyntaxTree.ParseText(Code);
        var diag = SyntaxTree.GetDiagnostics();
        PrintDiagnostics(diag);
        if (diag.Any(item => item.Severity == DiagnosticSeverity.Error)) throw new Error("parse error");

        // アセンブリ参照 雑に全部突っ込む
        var assemblies =
            AppDomain.CurrentDomain.GetAssemblies().
            Where(assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location));
        var assemblyLocations = assemblies.Select(assembly => assembly.Location);
        // コンパイル
        var comp = CSharpCompilation.Create(
            "Udon-Csharp-Assembly",
            syntaxTrees: new Microsoft.CodeAnalysis.SyntaxTree[] { SyntaxTree },
            references: assemblyLocations.Select(location => MetadataReference.CreateFromFile(location)),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            );
        var diag2 = comp.GetDiagnostics();
        PrintDiagnostics(diag2);
        if (diag2.Any(item => item.Severity == DiagnosticSeverity.Error)) throw new Error("compile error");

        SemanticModel = comp.GetSemanticModel(SyntaxTree);
    }

    void PrintDiagnostics(IEnumerable<Diagnostic> diagnostics) {
        if (!IsPrintDiagnostics) return;
        foreach (var item in diagnostics) {
            var span = item.Location.SourceSpan;
            var sub = Code.Substring(span.Start, span.Length);
            var message = $"[{item.Severity.ToString()}:{item.Id}] {span} {item.GetMessage()}\n{sub}";
            switch (item.Severity) {
                case DiagnosticSeverity.Error:
                    Debug.LogError(message);
                    break;
                case DiagnosticSeverity.Warning:
                    Debug.LogWarning(message);
                    break;
                default:
                    Debug.Log(message);
                    break;
            }
        }
    }

    public override void VisitFieldDeclaration(FieldDeclarationSyntax syntax) {
        var isSync = syntax.AttributeLists.Any(attribute =>attribute.Attributes.Any(a => a.Name.ToString() == "SerializeField"));
        var isPublic = syntax.Modifiers.Any(modifier => modifier.Kind() == SyntaxKind.PublicKeyword);
        var typeName = GetTypeNameOf(syntax.Declaration.Type);
        var nodeFullName = typeName.VariableName;
        foreach (var variable in syntax.Declaration.Variables) {
            var identifier = variable.Identifier.Text;
            var initializer = variable.Initializer?.Value;
            if (UdonNodeDefinitions.TryGetValue(nodeFullName, out var udonNodeDefinition)) {
                Debug.Log($"DEF: {udonNodeDefinition.fullName}");
                UdonGraph.CreateNode(udonNodeDefinition);
                // このノードに値をセットするところがかなりGUI依存で辛い。他に良い方法がないか？
                var node = UdonGraph.nodes.Last();
                var lackPropCount = 5 - node.properties.Count;
                for (var i = 0; i < lackPropCount; ++i) node.properties.Add(new UnityEditor.Graphs.Property());
                if (initializer != null) node.properties[0].value = initializer;
                node.properties[1].value = identifier;
                node.properties[2].value = isPublic;
                // node.properties[3].value = isSync;
            }
            Debug.Log($"{typeName.VariableName} {(isPublic ? "public" : "")} {(isSync ? "sync" : "")} {identifier} {initializer}");
        }
    }

    public override void VisitMethodDeclaration(MethodDeclarationSyntax node) {
        foreach (var statement in node.Body.Statements) {
            var kind = statement.Kind();
            Debug.Log($"{kind}> {statement.ToFullString()}");
        }
    }

    public void VisitNode(SyntaxNode syntax, int level = 0) {
        var kind = syntax.Kind();
        var str = syntax.ToFullString();
        Debug.Log($"NODE >{new string(' ', level)}{kind.ToString()}> {str}");
        // このへんでトークンを追ってUdonNodeに変換したい
        foreach (var node in syntax.ChildNodesAndTokens()) {
            if (node.IsNode) VisitNode(node.AsNode(), level + 1);
            if (node.IsToken) VisitToken(node.AsToken(), level + 1);
        }
    }

    public void VisitToken(SyntaxToken syntax, int level = 0) {
        var kind = syntax.Kind();
        var str = syntax.ToFullString();
        Debug.Log($"TOKEN>{new string(' ', level)}{kind.ToString()}> {str}");
    }

    TypeName GetTypeNameOf(TypeSyntax syntax) {
        var symbol = SemanticModel.GetSymbolInfo(syntax).Symbol as INamedTypeSymbol;
        return new TypeName(symbol.ToString());
    }
}
