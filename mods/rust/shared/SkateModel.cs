using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Shortcut.RustMod
{
    // A model read from a binary glTF (.glb) file the way the project's authored park pieces are
    // written: one buffer, meshes of triangle primitives with positions, normals, optional UVs and
    // indices, materials with a base colour, nodes with a translation/rotation/scale and the
    // project's `role` / `ride_surface` extras, and named scenes (the ramps' `Visual` and
    // `CollisionProxies`). Everything stays in glTF's own frame (metres, +Y up, +Z forward,
    // right-handed); the engine adapter converts. No engine types; errors are English strings.
    public sealed class SkateModelMesh
    {
        public readonly string Name;
        public readonly float[] Positions, Normals, UVs;   // xyz triples, xyz triples, uv pairs (UVs may be empty)
        public readonly int[] Triangles;                   // three vertex indices per triangle
        public readonly int Material;                      // -1 for none
        internal SkateModelMesh(string name, float[] positions, float[] normals, float[] uvs, int[] triangles, int material)
        { Name = name; Positions = positions; Normals = normals; UVs = uvs; Triangles = triangles; Material = material; }
        public int VertexCount { get { return Positions.Length / 3; } }
        public int TriangleCount { get { return Triangles.Length / 3; } }
    }

    public struct SkateModelMaterial
    {
        public readonly string Name;
        public readonly float Red, Green, Blue, Alpha, Metallic, Roughness;
        internal SkateModelMaterial(string name, float r, float g, float b, float a, float metallic, float roughness)
        { Name = name; Red = r; Green = g; Blue = b; Alpha = a; Metallic = metallic; Roughness = roughness; }
    }

    public sealed class SkateModelNode
    {
        public readonly string Name;
        public readonly int Mesh, Parent, Scene;           // mesh and parent are -1 for none; scene -1 when in none
        public readonly float X, Y, Z;                     // translation
        public readonly float QX, QY, QZ, QW;              // rotation, glTF order
        public readonly float SX, SY, SZ;                  // scale
        public readonly string Role;                       // the extras' role ("visual", "collision") or ""
        public readonly bool RideSurface;
        internal SkateModelNode(string name, int mesh, int parent, int scene, float x, float y, float z,
                                float qx, float qy, float qz, float qw, float sx, float sy, float sz, string role, bool ride)
        { Name = name; Mesh = mesh; Parent = parent; Scene = scene; X = x; Y = y; Z = z; QX = qx; QY = qy; QZ = qz; QW = qw;
          SX = sx; SY = sy; SZ = sz; Role = role; RideSurface = ride; }
    }

    public sealed class SkateModel
    {
        public const int MaximumBytes = 64 * 1024 * 1024;
        public const int MaximumVertices = 2000000, MaximumTriangles = 4000000, MaximumNodes = 4096;
        public readonly SkateModelMesh[] Meshes;
        public readonly SkateModelMaterial[] Materials;
        public readonly SkateModelNode[] Nodes;
        public readonly string[] Scenes;
        public readonly int DefaultScene;

        private SkateModel(SkateModelMesh[] meshes, SkateModelMaterial[] materials, SkateModelNode[] nodes, string[] scenes, int defaultScene)
        { Meshes = meshes; Materials = materials; Nodes = nodes; Scenes = scenes; DefaultScene = defaultScene; }

        public int SceneIndex(string name)
        {
            for (int i = 0; i < Scenes.Length; i++) if (Scenes[i] == name) return i;
            return -1;
        }

        public static bool TryParse(byte[] glb, out SkateModel model, out string error)
        {
            model = null; error = null;
            try
            {
                if (glb == null || glb.Length < 20 || glb.Length > MaximumBytes) { error = "The model file is empty, too small or larger than 64 MiB."; return false; }
                if (ReadUInt(glb, 0) != 0x46546C67u) { error = "The model file is not a binary glTF file."; return false; }
                if (ReadUInt(glb, 4) != 2u) { error = "The model file is not glTF version 2."; return false; }
                uint total = ReadUInt(glb, 8);
                if (total > (uint)glb.Length) { error = "The model file is shorter than its header says."; return false; }
                uint jsonLength = ReadUInt(glb, 12);
                if (ReadUInt(glb, 16) != 0x4E4F534Au || 20 + (long)jsonLength > total) { error = "The model file's first chunk is not JSON."; return false; }
                string json = Encoding.UTF8.GetString(glb, 20, (int)jsonLength);
                byte[] bin = null; int binOffset = 0, binLength = 0;
                long at = 20 + jsonLength;
                while (at + 8 <= total)
                {
                    uint length = ReadUInt(glb, (int)at), type = ReadUInt(glb, (int)at + 4);
                    if (at + 8 + length > total) { error = "A chunk of the model file runs past its end."; return false; }
                    if (type == 0x004E4942u && bin == null) { bin = glb; binOffset = (int)at + 8; binLength = (int)length; }
                    at += 8 + length;
                }
                object root = Json.Parse(json);
                var top = root as Dictionary<string, object>;
                if (top == null) { error = "The model's JSON is not an object."; return false; }
                return Build(top, bin, binOffset, binLength, out model, out error);
            }
            catch (Exception e) { model = null; error = "The model file could not be read: " + e.Message; return false; }
        }

        private static bool Build(Dictionary<string, object> top, byte[] bin, int binOffset, int binLength, out SkateModel model, out string error)
        {
            model = null; error = null;
            var buffers = List(top, "buffers"); var views = List(top, "bufferViews"); var accessors = List(top, "accessors");
            if (buffers.Count != 1 || bin == null) { error = "The model must carry exactly one buffer, inside the file."; return false; }
            var bufferLength = Int(Dict(buffers[0]), "byteLength", -1);
            if (bufferLength < 0 || bufferLength > binLength) { error = "The model's buffer is longer than its binary chunk."; return false; }

            var materialList = List(top, "materials");
            var materials = new SkateModelMaterial[materialList.Count];
            for (int i = 0; i < materials.Length; i++)
            {
                var m = Dict(materialList[i]);
                var pbr = m.ContainsKey("pbrMetallicRoughness") ? Dict(m["pbrMetallicRoughness"]) : new Dictionary<string, object>();
                float r = 1, g = 1, b = 1, a = 1;
                if (pbr.ContainsKey("baseColorFactor"))
                {
                    var c = List(pbr["baseColorFactor"]);
                    if (c.Count != 4) { error = "A material's base colour does not have four values."; return false; }
                    r = Float(c[0]); g = Float(c[1]); b = Float(c[2]); a = Float(c[3]);
                }
                float metallic = pbr.ContainsKey("metallicFactor") ? Float(pbr["metallicFactor"]) : 1f;
                float roughness = pbr.ContainsKey("roughnessFactor") ? Float(pbr["roughnessFactor"]) : 1f;
                if (!Unit(r) || !Unit(g) || !Unit(b) || !Unit(a) || !Unit(metallic) || !Unit(roughness)) { error = "A material's factors are outside 0..1."; return false; }
                materials[i] = new SkateModelMaterial(Str(m, "name"), r, g, b, a, metallic, roughness);
            }

            var meshList = List(top, "meshes");
            var meshes = new SkateModelMesh[meshList.Count];
            long vertices = 0, triangles = 0;
            for (int i = 0; i < meshes.Length; i++)
            {
                var m = Dict(meshList[i]);
                var primitives = List(m, "primitives");
                if (primitives.Count != 1) { error = "Mesh " + i + " must have exactly one primitive."; return false; }
                var p = Dict(primitives[0]);
                int mode = Int(p, "mode", 4);
                if (mode != 4) { error = "Mesh " + i + " is not a triangle list."; return false; }
                var attributes = Dict(p["attributes"]);
                float[] positions, normals, uvs = new float[0];
                if (!ReadFloats(accessors, views, bin, binOffset, bufferLength, Int(attributes, "POSITION", -1), 3, out positions, out error)) return false;
                if (!ReadFloats(accessors, views, bin, binOffset, bufferLength, Int(attributes, "NORMAL", -1), 3, out normals, out error)) return false;
                if (attributes.ContainsKey("TEXCOORD_0") && !ReadFloats(accessors, views, bin, binOffset, bufferLength, Int(attributes, "TEXCOORD_0", -1), 2, out uvs, out error)) return false;
                int count = positions.Length / 3;
                if (count == 0 || normals.Length != positions.Length || (uvs.Length != 0 && uvs.Length != count * 2)) { error = "Mesh " + i + " has mismatched vertex attributes."; return false; }
                int[] indices;
                if (!ReadIndices(accessors, views, bin, binOffset, bufferLength, Int(p, "indices", -1), count, out indices, out error)) return false;
                if (indices.Length == 0 || indices.Length % 3 != 0) { error = "Mesh " + i + " does not index whole triangles."; return false; }
                int material = Int(p, "material", -1);
                if (material < -1 || material >= materials.Length) { error = "Mesh " + i + " names a material the model does not have."; return false; }
                vertices += count; triangles += indices.Length / 3;
                if (vertices > MaximumVertices || triangles > MaximumTriangles) { error = "The model has more vertices or triangles than allowed."; return false; }
                meshes[i] = new SkateModelMesh(Str(m, "name"), positions, normals, uvs, indices, material);
            }

            var sceneList = List(top, "scenes");
            var scenes = new string[sceneList.Count];
            for (int i = 0; i < scenes.Length; i++) scenes[i] = Str(Dict(sceneList[i]), "name");
            int defaultScene = Int(top, "scene", scenes.Length > 0 ? 0 : -1);
            if (defaultScene < -1 || defaultScene >= scenes.Length) { error = "The default scene is not one of the model's scenes."; return false; }

            var nodeList = List(top, "nodes");
            if (nodeList.Count > MaximumNodes) { error = "The model has more nodes than allowed."; return false; }
            var parent = new int[nodeList.Count]; var scene = new int[nodeList.Count];
            for (int i = 0; i < parent.Length; i++) { parent[i] = -1; scene[i] = -1; }
            for (int i = 0; i < nodeList.Count; i++)
            {
                var n = Dict(nodeList[i]);
                if (!n.ContainsKey("children")) continue;
                foreach (var c in List(n["children"]))
                {
                    int child = (int)Float(c);
                    if (child < 0 || child >= nodeList.Count || child == i || parent[child] != -1) { error = "Node " + i + " has an invalid child."; return false; }
                    parent[child] = i;
                }
            }
            for (int s = 0; s < sceneList.Count; s++)
            {
                var sc = Dict(sceneList[s]);
                if (!sc.ContainsKey("nodes")) continue;
                var stack = new Stack<int>();
                foreach (var r in List(sc["nodes"]))
                {
                    int root = (int)Float(r);
                    if (root < 0 || root >= nodeList.Count || parent[root] != -1) { error = "Scene " + s + " has an invalid root node."; return false; }
                    stack.Push(root);
                }
                int visited = 0;
                while (stack.Count > 0)
                {
                    int i = stack.Pop();
                    if (scene[i] != -1) { error = "Node " + i + " is in two scenes or reached twice."; return false; }
                    scene[i] = s;
                    if (++visited > nodeList.Count) { error = "The model's node tree loops."; return false; }
                    var n = Dict(nodeList[i]);
                    if (n.ContainsKey("children")) foreach (var c in List(n["children"])) stack.Push((int)Float(c));
                }
            }
            var nodes = new SkateModelNode[nodeList.Count];
            for (int i = 0; i < nodes.Length; i++)
            {
                var n = Dict(nodeList[i]);
                int mesh = Int(n, "mesh", -1);
                if (mesh < -1 || mesh >= meshes.Length) { error = "Node " + i + " names a mesh the model does not have."; return false; }
                if (n.ContainsKey("matrix")) { error = "Node " + i + " uses a matrix; only translation, rotation and scale are read."; return false; }
                float x = 0, y = 0, z = 0, qx = 0, qy = 0, qz = 0, qw = 1, sx = 1, sy = 1, sz = 1;
                if (n.ContainsKey("translation")) { var t = List(n["translation"]); if (t.Count != 3) { error = "Node " + i + " has a bad translation."; return false; } x = Float(t[0]); y = Float(t[1]); z = Float(t[2]); }
                if (n.ContainsKey("rotation")) { var q = List(n["rotation"]); if (q.Count != 4) { error = "Node " + i + " has a bad rotation."; return false; } qx = Float(q[0]); qy = Float(q[1]); qz = Float(q[2]); qw = Float(q[3]); }
                if (n.ContainsKey("scale")) { var sc = List(n["scale"]); if (sc.Count != 3) { error = "Node " + i + " has a bad scale."; return false; } sx = Float(sc[0]); sy = Float(sc[1]); sz = Float(sc[2]); }
                double ql = Math.Sqrt((double)qx * qx + (double)qy * qy + (double)qz * qz + (double)qw * qw);
                if (!Finite(x) || !Finite(y) || !Finite(z) || !Finite(sx) || !Finite(sy) || !Finite(sz) || Math.Abs(ql - 1) > 0.001 ||
                    Math.Abs(x) > 100000 || Math.Abs(y) > 100000 || Math.Abs(z) > 100000 || sx <= 0 || sy <= 0 || sz <= 0 || sx > 1000 || sy > 1000 || sz > 1000)
                { error = "Node " + i + " has a transform outside the allowed range."; return false; }
                string role = ""; bool ride = false;
                if (n.ContainsKey("extras"))
                {
                    var extras = n["extras"] as Dictionary<string, object>;
                    if (extras != null)
                    {
                        role = Str(extras, "role");
                        object rs; ride = extras.TryGetValue("ride_surface", out rs) && rs is bool && (bool)rs;
                    }
                }
                nodes[i] = new SkateModelNode(Str(n, "name"), mesh, parent[i], scene[i], x, y, z, qx, qy, qz, qw, sx, sy, sz, role, ride);
            }
            model = new SkateModel(meshes, materials, nodes, scenes, defaultScene);
            return true;
        }

        private static bool ReadFloats(List<object> accessors, List<object> views, byte[] bin, int binOffset, int bufferLength,
                                       int accessor, int width, out float[] values, out string error)
        {
            values = null; error = null;
            Dictionary<string, object> a; int offset, stride, count;
            if (!Locate(accessors, views, bufferLength, accessor, 5126, width == 3 ? "VEC3" : "VEC2", width * 4, out a, out offset, out stride, out count, out error)) return false;
            values = new float[count * width];
            for (int i = 0; i < count; i++)
                for (int k = 0; k < width; k++)
                {
                    float v = BitConverter.ToSingle(bin, binOffset + offset + i * stride + k * 4);
                    if (!Finite(v) || Math.Abs(v) > 100000) { error = "Accessor " + accessor + " holds a value that is not a finite coordinate."; return false; }
                    values[i * width + k] = v;
                }
            return true;
        }

        private static bool ReadIndices(List<object> accessors, List<object> views, byte[] bin, int binOffset, int bufferLength,
                                        int accessor, int vertexCount, out int[] values, out string error)
        {
            values = null; error = null;
            if (accessor < 0 || accessor >= accessors.Count) { error = "A mesh has no index accessor."; return false; }
            int type = Int(Dict(accessors[accessor]), "componentType", -1);
            int size = type == 5123 ? 2 : type == 5125 ? 4 : type == 5121 ? 1 : 0;
            if (size == 0) { error = "Accessor " + accessor + " has an index type that is not read."; return false; }
            Dictionary<string, object> a; int offset, stride, count;
            if (!Locate(accessors, views, bufferLength, accessor, type, "SCALAR", size, out a, out offset, out stride, out count, out error)) return false;
            values = new int[count];
            for (int i = 0; i < count; i++)
            {
                int at = binOffset + offset + i * stride;
                int v = size == 2 ? BitConverter.ToUInt16(bin, at) : size == 4 ? (int)Math.Min(int.MaxValue, BitConverter.ToUInt32(bin, at)) : bin[at];
                if (v < 0 || v >= vertexCount) { error = "Accessor " + accessor + " indexes a vertex the mesh does not have."; return false; }
                values[i] = v;
            }
            return true;
        }

        private static bool Locate(List<object> accessors, List<object> views, int bufferLength, int accessor, int componentType, string type,
                                   int elementSize, out Dictionary<string, object> a, out int offset, out int stride, out int count, out string error)
        {
            a = null; offset = stride = count = 0; error = null;
            if (accessor < 0 || accessor >= accessors.Count) { error = "A mesh names an accessor the model does not have."; return false; }
            a = Dict(accessors[accessor]);
            if (Int(a, "componentType", -1) != componentType || Str(a, "type") != type || a.ContainsKey("sparse"))
            { error = "Accessor " + accessor + " is not of the expected kind."; return false; }
            count = Int(a, "count", -1);
            int view = Int(a, "bufferView", -1);
            if (count <= 0 || view < 0 || view >= views.Count) { error = "Accessor " + accessor + " has no buffer view or no elements."; return false; }
            var v = Dict(views[view]);
            if (Int(v, "buffer", -1) != 0) { error = "Buffer view " + view + " is not in the model's buffer."; return false; }
            int viewOffset = Int(v, "byteOffset", 0), viewLength = Int(v, "byteLength", -1);
            stride = Int(v, "byteStride", elementSize);
            offset = viewOffset + Int(a, "byteOffset", 0);
            if (viewOffset < 0 || viewLength < 0 || stride < elementSize || offset < viewOffset ||
                (long)viewOffset + viewLength > bufferLength || (long)(offset - viewOffset) + (long)(count - 1) * stride + elementSize > viewLength)
            { error = "Accessor " + accessor + " reaches outside its buffer view."; return false; }
            return true;
        }

        private static uint ReadUInt(byte[] b, int at) { return BitConverter.ToUInt32(b, at); }
        private static bool Finite(float v) { return !float.IsNaN(v) && !float.IsInfinity(v); }
        private static bool Unit(float v) { return Finite(v) && v >= 0f && v <= 1f; }
        private static Dictionary<string, object> Dict(object o)
        {
            var d = o as Dictionary<string, object>;
            if (d == null) throw new InvalidOperationException("an object was expected");
            return d;
        }
        private static List<object> List(object o)
        {
            var l = o as List<object>;
            if (l == null) throw new InvalidOperationException("an array was expected");
            return l;
        }
        private static List<object> List(Dictionary<string, object> d, string key)
        { object o; return d.TryGetValue(key, out o) ? List(o) : new List<object>(); }
        private static string Str(Dictionary<string, object> d, string key)
        { object o; return d.TryGetValue(key, out o) && o is string ? (string)o : ""; }
        private static int Int(Dictionary<string, object> d, string key, int fallback)
        {
            object o; if (!d.TryGetValue(key, out o) || !(o is double)) return fallback;
            double v = (double)o; if (v != Math.Floor(v) || v < int.MinValue || v > int.MaxValue) throw new InvalidOperationException(key + " is not an integer");
            return (int)v;
        }
        private static float Float(object o)
        {
            if (!(o is double)) throw new InvalidOperationException("a number was expected");
            return (float)(double)o;
        }

        // A reader for the JSON glTF writes: objects, arrays, strings (with the usual escapes),
        // numbers, true, false and null. Objects become dictionaries, arrays lists, numbers doubles.
        private sealed class Json
        {
            private readonly string text; private int at;
            private Json(string s) { text = s; }
            public static object Parse(string s)
            {
                var j = new Json(s); var v = j.Value(); j.Space();
                if (j.at != s.Length) throw new InvalidOperationException("trailing JSON");
                return v;
            }
            private void Space() { while (at < text.Length && (text[at] == ' ' || text[at] == '\t' || text[at] == '\n' || text[at] == '\r')) at++; }
            private char Next() { if (at >= text.Length) throw new InvalidOperationException("unexpected end of JSON"); return text[at]; }
            private void Expect(char c) { Space(); if (Next() != c) throw new InvalidOperationException("expected " + c); at++; }
            private object Value()
            {
                Space(); char c = Next();
                if (c == '{')
                {
                    at++; var d = new Dictionary<string, object>(); Space();
                    if (Next() == '}') { at++; return d; }
                    while (true)
                    {
                        Space(); var key = String(); Expect(':'); d[key] = Value(); Space();
                        char n = Next(); at++;
                        if (n == '}') return d;
                        if (n != ',') throw new InvalidOperationException("expected , in object");
                    }
                }
                if (c == '[')
                {
                    at++; var l = new List<object>(); Space();
                    if (Next() == ']') { at++; return l; }
                    while (true)
                    {
                        l.Add(Value()); Space();
                        char n = Next(); at++;
                        if (n == ']') return l;
                        if (n != ',') throw new InvalidOperationException("expected , in array");
                    }
                }
                if (c == '"') return String();
                if (c == 't') { Word("true"); return true; }
                if (c == 'f') { Word("false"); return false; }
                if (c == 'n') { Word("null"); return null; }
                int start = at;
                while (at < text.Length && ("+-0123456789.eE".IndexOf(text[at]) >= 0)) at++;
                if (start == at) throw new InvalidOperationException("unexpected JSON character");
                double v;
                if (!double.TryParse(text.Substring(start, at - start), NumberStyles.Float, CultureInfo.InvariantCulture, out v) || double.IsNaN(v) || double.IsInfinity(v))
                    throw new InvalidOperationException("bad JSON number");
                return v;
            }
            private void Word(string w)
            {
                if (at + w.Length > text.Length || string.CompareOrdinal(text, at, w, 0, w.Length) != 0) throw new InvalidOperationException("bad JSON literal");
                at += w.Length;
            }
            private string String()
            {
                Expect('"'); var sb = new StringBuilder();
                while (true)
                {
                    char c = Next(); at++;
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    char e = Next(); at++;
                    switch (e)
                    {
                        case '"': sb.Append('"'); break; case '\\': sb.Append('\\'); break; case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break; case 'f': sb.Append('\f'); break; case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break; case 't': sb.Append('\t'); break;
                        case 'u':
                            if (at + 4 > text.Length) throw new InvalidOperationException("bad JSON escape");
                            sb.Append((char)int.Parse(text.Substring(at, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture)); at += 4; break;
                        default: throw new InvalidOperationException("bad JSON escape");
                    }
                }
            }
        }
    }
}
