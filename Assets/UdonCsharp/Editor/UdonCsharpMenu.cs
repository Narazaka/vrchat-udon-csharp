using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.IO;

public class UdonCsharpMenu {
    [MenuItem("Assets/Compile to Udon")]
    static void CompileToUdon () {
        var path = AssetDatabase.GetAssetPath(Selection.activeObject);
        var udonCsharp = new UdonCsharp(File.ReadAllText(path));
        udonCsharp.Compile();
        var asset = ScriptableObject.CreateInstance<UdonGraphProgramAsset>();
        udonCsharp.UdonGraph.SetDirty();
        asset.graphData = udonCsharp.UdonGraph.data;
        AssetDatabase.CreateAsset(asset, path + ".asset");
    }

    [MenuItem("Assets/Compile to Udon", true)]
    static bool CompileToUdonValidate() {
        return Selection.activeObject is MonoScript;
    }
}
