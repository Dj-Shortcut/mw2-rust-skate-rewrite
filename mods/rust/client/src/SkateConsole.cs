// A console command typed for the player. The game's own console class is renamed in this client
// build and could not be found by its shape, so the plugin does what a hand does: it sends the
// game window the keys through Windows (SendInput) — F1 opens the console, the line is typed as
// characters, Enter runs it, F1 closes the console again. One step a frame or two apart, because
// the console takes a frame to open and to take focus. The game must be the window in front, which
// it is while someone plays. Where SendInput is not there (not Windows), the line goes on the
// clipboard instead and the screen says so.
using System;
using System.Runtime.InteropServices;
using UnityEngine;
using BepInEx;

public static class SkateConsole
{
    public const float StepSeconds = 0.12f;
    // The game reads keys by their scan code (raw input), so a key is sent as a scan code: a virtual
    // key alone arrives with scan code 0 and the game sees no key at all.
    private const ushort VkF1 = 0x70, VkReturn = 0x0D, ScanF1 = 0x3B, ScanReturn = 0x1C;
    private const uint InputKeyboard = 1, KeyUp = 0x0002, Unicode = 0x0004, ScanCode = 0x0008;

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput { public ushort Vk, Scan; public uint Flags, Time; public IntPtr Extra; }
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct Input { [FieldOffset(0)] public uint Type; [FieldOffset(8)] public KeyboardInput Key; }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);

    public static Func<string, bool> Typist;
    // A check leaves the console open, so the answer is on the screen.
    public static bool KeepOpen;
    // Left open by the last line (KeepOpen): the next line must not press F1, which would close it.
    public static bool LeftOpen;
    public static int Typed, Failed;
    public static string Last = "";
    private static string pending = "";
    private static int step = -1;
    private static float nextAt;
    private static bool said;

    // Takes the line for typing; false when it is already busy with one or typing is not available.
    public static bool Queue(string line, float now)
    {
        if (Typist != null) { var ok = Typist(line); if (ok) { Typed++; Last = line; } else Failed++; return ok; }
        if (step >= 0) return false;
        pending = line; step = 0; nextAt = now; Last = line;
        return true;
    }

    public static bool Busy { get { return step >= 0; } }

    public static void Update(float now)
    {
        if (step < 0 || now < nextAt) return;
        try
        {
            if (step == 0) { if (!LeftOpen) Tap(VkF1, ScanF1); }
            else if (step == 1) Text(pending);
            else if (step == 2) Tap(VkReturn, ScanReturn);
            else if (step == 3) { if (!KeepOpen) Tap(VkF1, ScanF1); LeftOpen = KeepOpen; Typed++; Out.Say("CONSOLE typed: " + pending); }
            step = step < 3 ? step + 1 : -1; nextAt = now + StepSeconds;
        }
        catch (Exception e)
        {
            step = -1; Failed++;
            if (!said) { said = true; Out.Say("CONSOLE typing is not available here: " + e.GetType().Name + ": " + e.Message); }
            try { GUIUtility.systemCopyBuffer = pending; Out.Say("CONSOLE on the clipboard instead: " + pending); } catch (Exception) { }
        }
    }

    private static void Tap(ushort vk, ushort scan)
    {
        var inputs = new Input[2];
        inputs[0].Type = InputKeyboard; inputs[0].Key.Vk = vk; inputs[0].Key.Scan = scan; inputs[0].Key.Flags = ScanCode;
        inputs[1].Type = InputKeyboard; inputs[1].Key.Vk = vk; inputs[1].Key.Scan = scan; inputs[1].Key.Flags = ScanCode | KeyUp;
        Send(inputs);
    }

    private static void Text(string s)
    {
        var inputs = new Input[s.Length * 2];
        for (var i = 0; i < s.Length; i++)
        {
            inputs[2 * i].Type = InputKeyboard; inputs[2 * i].Key.Scan = s[i]; inputs[2 * i].Key.Flags = Unicode;
            inputs[2 * i + 1].Type = InputKeyboard; inputs[2 * i + 1].Key.Scan = s[i]; inputs[2 * i + 1].Key.Flags = Unicode | KeyUp;
        }
        Send(inputs);
    }

    private static void Send(Input[] inputs)
    {
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(Input)));
        if (sent != inputs.Length) throw new InvalidOperationException("SendInput sent " + sent + " of " + inputs.Length + " (error " + Marshal.GetLastWin32Error() + ")");
    }

    public static void Reset() { pending = ""; step = -1; Typed = Failed = 0; Last = ""; LeftOpen = false; SentLines = 0; SendState = ""; sendPrepared = sendFailed = false; }

    // A command sent to the server the way the game's own console sends one: a ConsoleCommand
    // packet on the player's connection, written with the network library (Facepunch.Network keeps
    // its names; the game's console class does not). Found by name at run time, so a client build
    // without these names refuses here instead of failing to compile; SendState says what was found.
    public static int SentLines;
    public static string SendState = "";
    private static object connection, netClient;
    private static System.Reflection.MethodInfo startWrite, packetId, writeString, sendWrite;
    private static object consoleCommandId;
    private static Type sendInfoType;
    private static int writeStringExtra;
    private static bool sendPrepared, sendFailed;
    // For the offline check of the shape search: the names are not tried.
    public static bool ByShapeOnly;

    public static bool Send(string line)
    {
        if (string.IsNullOrEmpty(line) || line.Length > 400 || line.IndexOf('\n') >= 0) { SendState = "refused: an empty, overlong or multi-line command"; return false; }
        try
        {
            if (!Prepare()) return false;
            var write = startWrite.Invoke(netClient, null);
            if (write == null) { SendState = "the network client gave no write"; return false; }
            packetId.Invoke(write, new[] { consoleCommandId });
            writeString.Invoke(write, writeStringExtra == 0 ? new object[] { line } : new object[] { line, false });
            var info = Activator.CreateInstance(sendInfoType, connection);
            sendWrite.Invoke(write, new[] { info });
            SentLines++; Last = line; SendState = "sent";
            Out.Say("CONSOLE sent to the server: " + line);
            return true;
        }
        catch (Exception e)
        {
            SendState = "sending threw " + e.GetType().Name + ": " + (e.InnerException != null ? e.InnerException.Message : e.Message);
            Out.Say("CONSOLE " + SendState);
            return false;
        }
    }

    public static bool CanSend { get { return Prepare(); } }

    private static bool Prepare()
    {
        if (sendPrepared) return !sendFailed;
        sendPrepared = true; sendFailed = true;
        try
        {
            var net = ByShapeOnly ? null : FindType("Network.Net");
            if (net == null)
            {
                // The names are gone in this build: the same path found by its shape instead.
                var shaped = PrepareByShape();
                if (shaped == "") return true;
                SendState = "no Network.Net type, and by shape: " + shaped + " (network assemblies loaded: " + NetworkAssemblies() + "; " + DumpNetwork() + ")";
                return false;
            }
            var cl = net.GetField("cl", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            var clValue = cl != null ? cl.GetValue(null) : null;
            if (clValue == null)
            {
                var clProperty = net.GetProperty("cl", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                clValue = clProperty != null ? clProperty.GetValue(null, null) : null;
            }
            if (clValue == null) { SendState = "Network.Net.cl is not there or not connected"; sendPrepared = false; return false; }
            netClient = clValue;
            var clientType = clValue.GetType();
            var connectionMember = clientType.GetProperty("Connection") as System.Reflection.MemberInfo ?? clientType.GetField("Connection");
            connection = connectionMember is System.Reflection.PropertyInfo ? ((System.Reflection.PropertyInfo)connectionMember).GetValue(clValue, null)
                : connectionMember is System.Reflection.FieldInfo ? ((System.Reflection.FieldInfo)connectionMember).GetValue(clValue) : null;
            if (connection == null) { SendState = "the network client has no connection yet"; sendPrepared = false; return false; }
            startWrite = clientType.GetMethod("StartWrite", Type.EmptyTypes);
            if (startWrite == null) { SendState = "no StartWrite on the network client"; return false; }
            var writeType = startWrite.ReturnType;
            var messageType = FindType("Network.Message+Type") ?? FindType("Network.Message/Type");
            if (messageType == null) { var message = FindType("Network.Message"); messageType = message != null ? message.GetNestedType("Type") : null; }
            if (messageType == null) { SendState = "no Network.Message.Type enum"; return false; }
            consoleCommandId = Enum.Parse(messageType, "ConsoleCommand");
            packetId = writeType.GetMethod("PacketID", new[] { messageType });
            // The string write may take a second, optional argument in some builds.
            writeString = null; writeStringExtra = 0;
            foreach (var m in writeType.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                if (m.Name != "String" && m.Name != "WriteString") continue;
                var ps = m.GetParameters();
                if (ps.Length < 1 || ps.Length > 2 || ps[0].ParameterType != typeof(string)) continue;
                if (ps.Length == 2 && ps[1].ParameterType != typeof(bool)) continue;
                if (writeString == null || ps.Length < writeStringExtra + 1) { writeString = m; writeStringExtra = ps.Length - 1; }
            }
            sendInfoType = FindType("Network.SendInfo");
            if (packetId == null || writeString == null || sendInfoType == null) { SendState = "the network write lacks PacketID, String or SendInfo"; return false; }
            sendWrite = writeType.GetMethod("Send", new[] { sendInfoType });
            if (sendWrite == null) { SendState = "the network write has no Send(SendInfo)"; return false; }
            if (sendInfoType.GetConstructor(new[] { connection.GetType() }) == null && sendInfoType.GetConstructor(new[] { FindType("Network.Connection") ?? connection.GetType() }) == null)
            { SendState = "SendInfo has no constructor taking a connection"; return false; }
            sendFailed = false; SendState = "ready";
            return true;
        }
        catch (Exception e) { SendState = "preparing threw " + e.GetType().Name + ": " + e.Message; return false; }
    }

    // The console path found by its shape, for a build that renamed the network library's types:
    // the packet enum is the one with a ConsoleCommand member; the write type has a method taking
    // that enum (PacketID), one taking a string (String), and one taking a struct that is built
    // from a connection (Send(SendInfo)); the client type has a parameterless method returning the
    // write (StartWrite) and holds a connection; and a static field or property somewhere holds the
    // client (Net.cl). Returns "" when every piece was found and the connection is there, otherwise
    // what is missing. The names found are logged, so a session's log says what the build calls them.
    private static string PrepareByShape()
    {
        var enums = 0; Type messageType = null;
        var all = new System.Collections.Generic.List<Type>();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            // The game's own assemblies only: the engine, the runtime and the loader are skipped.
            string name; try { name = assembly.GetName().Name; } catch (Exception) { continue; }
            if (name.StartsWith("System") || name.StartsWith("mscorlib") || name.StartsWith("netstandard") || name.StartsWith("Microsoft") || name.StartsWith("Il2Cpp") || name.StartsWith("BepInEx") || name.StartsWith("Unity") || name.StartsWith("Mono") || name.StartsWith("0Harmony") || name.StartsWith("ShortcutSkate")) continue;
            Type[] types; try { types = assembly.GetTypes(); } catch (System.Reflection.ReflectionTypeLoadException e) { types = e.Types; } catch (Exception) { continue; }
            foreach (var t in types) if (t != null) all.Add(t);
        }
        // An obfuscated build can carry more than one enum with that member: each is tried.
        var candidates = new System.Collections.Generic.List<Type>();
        foreach (var t in all)
        {
            if (!t.IsEnum) continue;
            enums++;
            try { if (Enum.IsDefined(t, "ConsoleCommand")) candidates.Add(t); } catch (Exception) { }
        }
        if (candidates.Count == 0) return "no enum with a ConsoleCommand member among " + enums + " enums";
        var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
        Type writeType = null; System.Reflection.MethodInfo foundPacketId = null, foundString = null, foundSend = null; Type foundSendInfo = null;
        var tried = new System.Text.StringBuilder();
        foreach (var cand in candidates)
        {
        messageType = cand;
        foreach (var w in all)
        {
            if (!w.IsClass || w.IsAbstract) continue;
            System.Reflection.MethodInfo pid = null, str = null, snd = null; Type info = null; var extra = 0;
            System.Reflection.MethodInfo[] methods; try { methods = w.GetMethods(flags); } catch (Exception) { continue; }
            foreach (var m in methods)
            {
                var ps = m.GetParameters();
                if (ps.Length == 1 && ps[0].ParameterType == messageType && pid == null) pid = m;
                if (ps.Length >= 1 && ps.Length <= 2 && ps[0].ParameterType == typeof(string) && (ps.Length == 1 || ps[1].ParameterType == typeof(bool)) && m.ReturnType == typeof(void) && (str == null || ps.Length < extra + 1)) { str = m; extra = ps.Length - 1; }
                if (ps.Length == 1 && ps[0].ParameterType.IsValueType && !ps[0].ParameterType.IsPrimitive && !ps[0].ParameterType.IsEnum && snd == null)
                {
                    // The struct is built from a single reference: the connection.
                    foreach (var c in ps[0].ParameterType.GetConstructors())
                    {
                        var cps = c.GetParameters();
                        if (cps.Length == 1 && cps[0].ParameterType.IsClass && !cps[0].ParameterType.IsGenericType && cps[0].ParameterType != typeof(string)) { snd = m; info = ps[0].ParameterType; break; }
                    }
                }
            }
            if (pid != null && str != null && snd != null) { writeType = w; foundPacketId = pid; foundString = str; foundSend = snd; foundSendInfo = info; writeStringExtra = extra; break; }
        }
        if (writeType != null) break;
        tried.Append(tried.Length > 0 ? " | " : "").Append(messageType.Assembly.GetName().Name).Append(':').Append(messageType.FullName).Append(' ').Append(Taking(all, messageType));
        }
        if (writeType == null) return "no write type with a method taking the packet enum, one taking a string and one taking a connection-built struct; " + candidates.Count + " enums with ConsoleCommand: " + tried;
        Type clientType = null; System.Reflection.MethodInfo foundStart = null; System.Reflection.MemberInfo connectionMember = null;
        foreach (var k in all)
        {
            if (!k.IsClass) continue;
            System.Reflection.MethodInfo start = null;
            try { foreach (var m in k.GetMethods(flags)) if (m.GetParameters().Length == 0 && m.ReturnType == writeType) { start = m; break; } } catch (Exception) { continue; }
            if (start == null) continue;
            System.Reflection.MemberInfo holder = null;
            try
            {
                foreach (var f in k.GetFields(flags)) if (f.FieldType.IsClass && f.FieldType != typeof(string) && f.FieldType != k && ConnectionLike(f.FieldType, foundSendInfo)) { holder = f; break; }
                if (holder == null) foreach (var pr in k.GetProperties(flags)) if (pr.PropertyType.IsClass && pr.PropertyType != typeof(string) && pr.PropertyType != k && pr.GetIndexParameters().Length == 0 && ConnectionLike(pr.PropertyType, foundSendInfo)) { holder = pr; break; }
            }
            catch (Exception) { continue; }
            if (holder == null) continue;
            clientType = k; foundStart = start; connectionMember = holder; break;
        }
        if (clientType == null) return "no client type with a parameterless method returning " + writeType.FullName + " and a connection";
        object clientValue = null; string where = "";
        var staticFlags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static;
        foreach (var h in all)
        {
            try
            {
                foreach (var f in h.GetFields(staticFlags)) if (clientType.IsAssignableFrom(f.FieldType)) { var v = f.GetValue(null); if (v != null) { clientValue = v; where = h.FullName + "." + f.Name; break; } }
                if (clientValue == null) foreach (var pr in h.GetProperties(staticFlags)) if (clientType.IsAssignableFrom(pr.PropertyType) && pr.GetIndexParameters().Length == 0) { var v = pr.GetValue(null, null); if (v != null) { clientValue = v; where = h.FullName + "." + pr.Name; break; } }
            }
            catch (Exception) { }
            if (clientValue != null) break;
        }
        if (clientValue == null) { sendPrepared = false; return "no static field or property holds a " + clientType.FullName + " yet (not connected?)"; }
        var conn = connectionMember is System.Reflection.PropertyInfo ? ((System.Reflection.PropertyInfo)connectionMember).GetValue(clientValue, null) : ((System.Reflection.FieldInfo)connectionMember).GetValue(clientValue);
        if (conn == null) { sendPrepared = false; return "the client at " + where + " has no connection yet"; }
        netClient = clientValue; connection = conn; startWrite = foundStart; packetId = foundPacketId; writeString = foundString; sendWrite = foundSend; sendInfoType = foundSendInfo;
        consoleCommandId = Enum.Parse(messageType, "ConsoleCommand");
        sendFailed = false; SendState = "ready";
        Out.Say("CONSOLE the console path by shape: client " + where + " (" + clientType.FullName + "), write " + writeType.FullName + "." + foundStart.Name + "(), packet " + foundPacketId.Name + "(" + messageType.FullName + "), string " + foundString.Name + ", send " + foundSend.Name + "(" + foundSendInfo.FullName + "), connection " + connectionMember.Name);
        return "";
    }

    // Whether a type is what the send struct is built from: the parameter type of one of its
    // single-argument constructors, or a base of it. A list of connections (the server's side has
    // such a constructor too) is not a connection.
    private static bool ConnectionLike(Type t, Type sendInfo)
    {
        if (t.IsGenericType) return false;
        foreach (var c in sendInfo.GetConstructors())
        {
            var cps = c.GetParameters();
            if (cps.Length != 1 || !cps[0].ParameterType.IsClass || cps[0].ParameterType.IsGenericType) continue;
            if (cps[0].ParameterType.IsAssignableFrom(t) || t.IsAssignableFrom(cps[0].ParameterType)) return true;
        }
        return false;
    }

    // The network library's interop assembly is loaded only when something refers to it: it is
    // asked for by name first, then by its file next to the other interop assemblies.
    private static bool networkLoaded;
    private static Type FindType(string name)
    {
        if (!networkLoaded)
        {
            networkLoaded = true;
            try { System.Reflection.Assembly.Load("Facepunch.Network"); }
            catch (Exception)
            {
                try
                {
                    var interop = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Paths.PluginPath.TrimEnd('\\', '/')), "interop");
                    var file = System.IO.Path.Combine(interop, "Facepunch.Network.dll");
                    if (System.IO.File.Exists(file)) System.Reflection.Assembly.LoadFrom(file);
                    else Out.Say("CONSOLE no Facepunch.Network.dll at " + file);
                }
                catch (Exception e) { Out.Say("CONSOLE loading the network library failed: " + e.GetType().Name + ": " + e.Message); }
            }
        }
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try { var t = assembly.GetType(name, false); if (t != null) return t; } catch (Exception) { }
        }
        return null;
    }

    // What the network library's interop assembly holds, written next to the log once: every type
    // with its public static members and method names, so a renamed console path can be found by
    // its shape. Returns where the listing went.
    private static string DumpNetwork()
    {
        try
        {
            var sb = new System.Text.StringBuilder();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string name; try { name = assembly.GetName().Name; } catch (Exception) { continue; }
                if (name != "Facepunch.Network") continue;
                Type[] types; try { types = assembly.GetTypes(); } catch (System.Reflection.ReflectionTypeLoadException e) { types = e.Types; }
                foreach (var t in types)
                {
                    if (t == null) continue;
                    sb.Append("TYPE ").Append(t.FullName).Append(t.IsEnum ? " enum" : t.IsValueType ? " struct" : t.IsAbstract && t.IsSealed ? " static" : "").Append('\n');
                    try
                    {
                        if (t.IsEnum)
                        {
                            foreach (var en in Enum.GetNames(t)) sb.Append("  ").Append(en).Append('=').Append(Convert.ToInt64(Enum.Parse(t, en))).Append('\n');
                            continue;
                        }
                        foreach (var f in t.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
                            sb.Append("  ").Append(f.IsStatic ? "static field " : "field ").Append(f.FieldType.Name).Append(' ').Append(f.Name).Append('\n');
                        foreach (var pr in t.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
                            sb.Append("  ").Append(pr.GetGetMethod() != null && pr.GetGetMethod().IsStatic ? "static property " : "property ").Append(pr.PropertyType.Name).Append(' ').Append(pr.Name).Append('\n');
                        foreach (var m in t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
                        {
                            if (m.IsSpecialName) continue;
                            sb.Append("  ").Append(m.IsStatic ? "static " : "").Append(m.ReturnType.Name).Append(' ').Append(m.Name).Append('(');
                            var ps = m.GetParameters();
                            for (var i = 0; i < ps.Length; i++) sb.Append(i > 0 ? ", " : "").Append(ps[i].ParameterType.Name);
                            sb.Append(")\n");
                        }
                    }
                    catch (Exception e) { sb.Append("  (members unreadable: ").Append(e.GetType().Name).Append(")\n"); }
                }
            }
            var path = System.IO.Path.Combine(Paths.PluginPath, "console-net.txt");
            System.IO.File.WriteAllText(path, sb.ToString());
            return "listing in " + path + ", " + sb.Length + " chars";
        }
        catch (Exception e) { return "no listing: " + e.GetType().Name + ": " + e.Message; }
    }

    // Where the packet enum is used at all: every method, in any game assembly, with a parameter or a
    // return of that type (at most a dozen), for the log when the write type is not found.
    private static string Taking(System.Collections.Generic.List<Type> all, Type messageType)
    {
        var sb = new System.Text.StringBuilder("methods using " + messageType.Name + ":"); var n = 0;
        var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly;
        foreach (var t in all)
        {
            System.Reflection.MethodInfo[] ms; try { ms = t.GetMethods(flags); } catch (Exception) { continue; }
            foreach (var m in ms)
            {
                System.Reflection.ParameterInfo[] ps; try { ps = m.GetParameters(); } catch (Exception) { continue; }
                var uses = m.ReturnType == messageType;
                foreach (var pi in ps) if (pi.ParameterType == messageType) uses = true;
                if (!uses) continue;
                sb.Append(n == 0 ? " " : "; ").Append(t.FullName).Append('.').Append(m.Name).Append('(');
                for (var i = 0; i < ps.Length; i++) sb.Append(i > 0 ? "," : "").Append(ps[i].ParameterType.Name);
                sb.Append(")->").Append(m.ReturnType.Name);
                if (++n >= 12) return sb.ToString();
            }
        }
        return n == 0 ? sb.Append(" none").ToString() : sb.ToString();
    }

    // The loaded assemblies whose names mention the network, for the log when a type is missing.
    private static string NetworkAssemblies()
    {
        var names = new System.Collections.Generic.List<string>();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try { var n = assembly.GetName().Name; if (n.IndexOf("Network", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Facepunch", StringComparison.OrdinalIgnoreCase) >= 0) names.Add(n); } catch (Exception) { }
        }
        return names.Count == 0 ? "none" : string.Join(",", names.ToArray());
    }
}
