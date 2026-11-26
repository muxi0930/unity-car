using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Rendering;

// Each batch stores one material and the source instances in world space.
// Convert to native Unity meshes at import, so players need no model loader.
[ScriptedImporter(4, "foxmesh")]
public sealed class FoxMeshImporter : ScriptedImporter
{
    static string ReadName(BinaryReader reader) =>
        Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32()));

    static Vector3 ReadPoint(BinaryReader reader) =>
        new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

    public override void OnImportAsset(AssetImportContext ctx)
    {
        using var file = File.OpenRead(ctx.assetPath);
        using var stream = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new BinaryReader(stream);
        var root = new GameObject(Path.GetFileNameWithoutExtension(ctx.assetPath));
        ctx.AddObjectToAsset("root", root);
        ctx.SetMainObject(root);
        int count = reader.ReadInt32();
        if (count == -2)
        {
            ImportWorld(ctx, root, reader);
            return;
        }
        for (int i = 0; i < count; i++)
        {
            var part = new GameObject(ReadName(reader));
            part.transform.SetParent(root.transform, false);
            var color = ReadPoint(reader);
            const string shaderPath = "Packages/com.unity.render-pipelines.universal/Shaders/Lit.shader";
            ctx.DependsOnArtifact(shaderPath);
            var material = new Material(AssetDatabase.LoadAssetAtPath<Shader>(shaderPath)) {name = part.name};
            material.color = new Color(color.x, color.y, color.z);
            material.SetFloat("_Metallic", reader.ReadSingle());
            material.SetFloat("_Smoothness", reader.ReadSingle());
            string texture = ReadName(reader);
            if (texture.Length > 0)
            {
                string path = $"Assets/Art/{texture}.png";
                ctx.DependsOnArtifact(path);
                material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            bool curbs = reader.ReadInt32() != 0;
            int vertices = reader.ReadInt32();
            var points = new Vector3[vertices];
            var normals = new Vector3[vertices];
            var uv = new Vector2[vertices];
            for (int j = 0; j < vertices; j++) points[j] = ReadPoint(reader);
            for (int j = 0; j < vertices; j++) normals[j] = ReadPoint(reader);
            for (int j = 0; j < vertices; j++) uv[j] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
            byte[] stripe = curbs ? reader.ReadBytes(vertices) : null;
            var triangles = new int[reader.ReadInt32()];
            for (int j = 0; j < triangles.Length; j++) triangles[j] = reader.ReadInt32();
            // Unity rasterizes clockwise front faces. Keep lighting normals outward.
            for (int j = 0; j < triangles.Length; j += 3)
                (triangles[j+1], triangles[j+2]) = (triangles[j+2], triangles[j+1]);
            var mesh = new Mesh {name = part.name, indexFormat = vertices > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16};
            mesh.vertices = points;
            mesh.normals = normals;
            mesh.uv = uv;
            var renderer = part.AddComponent<MeshRenderer>();
            if (curbs)
            {
                var groups = new[] {new List<int>(), new List<int>()};
                foreach (int vertex in triangles) groups[stripe[vertex]].Add(vertex);
                mesh.subMeshCount = 2;
                for (int j = 0; j < groups.Length; j++) mesh.SetTriangles(groups[j], j);
                var red = new Material(material) {name = "Curb red", color = new Color32(182,67,58,255)};
                material.color = new Color32(232,226,210,255);
                ctx.AddObjectToAsset("curb-red", red);
                renderer.sharedMaterials = new[] {material, red};
            }
            else
            {
                mesh.triangles = triangles;
                renderer.sharedMaterial = material;
            }
            mesh.RecalculateBounds();
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            ctx.AddObjectToAsset("mesh"+i, mesh);
            ctx.AddObjectToAsset("material"+i, material);
        }
    }

    static void ImportWorld(AssetImportContext ctx, GameObject root, BinaryReader reader)
    {
        const string litPath = "Packages/com.unity.render-pipelines.universal/Shaders/Lit.shader";
        const string vertexPath = "Assets/Resources/Photon/VertexSurface.shader";
        ctx.DependsOnArtifact(litPath);
        ctx.DependsOnArtifact(vertexPath);
        var lit = AssetDatabase.LoadAssetAtPath<Shader>(litPath);
        var vertex = AssetDatabase.LoadAssetAtPath<Shader>(vertexPath);
        int batches = reader.ReadInt32();
        for (int i = 0; i < batches; i++)
        {
            var name = ReadName(reader);
            var color = ReadPoint(reader);
            float metallic = reader.ReadSingle(), smoothness = reader.ReadSingle(), opacity = reader.ReadSingle();
            string tex = ReadName(reader);
            int flags = reader.ReadInt32();
            bool unlit = (flags & 1) != 0, twoSided = (flags & 2) != 0, transparent = (flags & 4) != 0;
            bool lines = (flags & 8) != 0, vertexColor = reader.ReadInt32() != 0;
            bool custom = unlit || vertexColor || transparent;
            var material = new Material(custom ? vertex : lit) { name = (flags & 16) != 0 ? "Driver identity" : name };
            material.SetColor("_BaseColor", new Color(color.x, color.y, color.z, opacity));
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Cull", twoSided ? (int)CullMode.Off : (int)CullMode.Back);
            if (custom) material.SetFloat("_Unlit", unlit ? 1 : 0);
            if (transparent)
            {
                material.SetFloat("_SrcBlend", (int)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0);
                material.renderQueue = (int)RenderQueue.Transparent;
                material.SetOverrideTag("RenderType", "Transparent");
            }
            if (tex.Length > 0)
            {
                string path = $"Assets/Resources/Photon/Textures/{tex}.png";
                ctx.DependsOnArtifact(path);
                material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(path));
            }
            int vertices = reader.ReadInt32();
            var positions = new Vector3[vertices];
            var normals = new Vector3[vertices];
            var uv = new Vector2[vertices];
            for (int j = 0; j < vertices; j++) positions[j] = ReadPoint(reader);
            for (int j = 0; j < vertices; j++) normals[j] = ReadPoint(reader);
            for (int j = 0; j < vertices; j++) uv[j] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
            Color32[] colors = custom ? new Color32[vertices] : null;
            if (colors != null)
                for (int j = 0; j < vertices; j++)
                    colors[j] = vertexColor ? new Color32(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte()) :
                        new Color32(255, 255, 255, 255);
            var indices = new int[reader.ReadInt32()];
            for (int j = 0; j < indices.Length; j++) indices[j] = reader.ReadInt32();
            if (!lines)
                for (int j = 0; j < indices.Length; j += 3)
                    (indices[j+1], indices[j+2]) = (indices[j+2], indices[j+1]);
            var mesh = new Mesh { name = name, indexFormat = vertices > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.vertices = positions;
            mesh.normals = normals;
            mesh.uv = uv;
            if (colors != null) mesh.colors32 = colors;
            mesh.SetIndices(indices, lines ? MeshTopology.Lines : MeshTopology.Triangles, 0);
            var part = new GameObject($"{name} ({i})");
            part.transform.SetParent(root.transform, false);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = part.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            if (unlit || transparent) renderer.shadowCastingMode = ShadowCastingMode.Off;
            ctx.AddObjectToAsset("mesh"+i, mesh);
            ctx.AddObjectToAsset("material"+i, material);
        }
    }
}
