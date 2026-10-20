
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal sealed class Edit
{
    public int Offset;
    public byte[] Bytes;
    public string Why;
    public Edit(int off, byte[] bytes, string why) { Offset = off; Bytes = bytes; Why = why; }
}

internal static class Program
{
    const string DlLoaderType  = "PCL.ModDownload";
    const string DlLoaderField = "DlClientListLoader";
    const string InstallPage   = "PCL.PageDownloadInstall";
    const string InstallInit   = "LoaderInit";
    const string SecretType    = "PCL.ModSecret";
    const string PermGetter    = "get_HasUpdatePermission";
    const string CheckMethod   = "UpdateCheckByButton";
    const string ReplaceMethod = "UpdateReplace";
    const string DummyTaskName = "\u5173\u95ed\u4e0b\u8f7d";

    static readonly Regex RestrictText = new Regex(
        "(\u4e0d\u518d\u63d0\u4f9b|\u65e0\u6cd5\u63d0\u4f9b|\u505c\u6b62\u63d0\u4f9b|\u6682\u505c\u63d0\u4f9b|\u4e0d\u518d\u652f\u6301).{0,60}\u4e0b\u8f7d|" +
        "\u4e0b\u8f7d.{0,60}(\u4e0d\u518d\u63d0\u4f9b|\u65e0\u6cd5\u63d0\u4f9b|\u505c\u6b62\u63d0\u4f9b)",
        RegexOptions.Compiled | RegexOptions.Singleline);

    static int _errors = 0;

    static int Main(string[] args)
    {
        InstallResolver();
        AppDomain.CurrentDomain.UnhandledException += OnUnhandled;
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }
        Say("==============================================");
        Say(" PCL2  MC \u4e0b\u8f7d\u89e3\u9501 / \u66f4\u65b0\u5c4f\u853d  \u6ce8\u5165\u5de5\u5177");
        Say("==============================================");

        bool blockUpdate = false, unlockOnly = false, uninstall = false, dryRun = false, noBackup = false;
        string outPath = null;
        var files = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            if (a == "--block-update") blockUpdate = true;
            else if (a == "--unlock-only" || a == "--keep-update") unlockOnly = true;
            else if (a == "--uninstall") uninstall = true;
            else if (a == "--dry-run") dryRun = true;
            else if (a == "--no-backup") noBackup = true;
            else if (a == "--out" && i + 1 < args.Length) outPath = args[++i];
            else if (a.StartsWith("-")) { Say("[\u9519\u8bef] \u672a\u77e5\u53c2\u6570\uff1a" + a); return 2; }
            else files.Add(a);
        }
        bool modeExplicit = blockUpdate || unlockOnly || uninstall;
        bool canAsk = false;
        try { canAsk = Environment.UserInteractive && !Console.IsInputRedirected; } catch { canAsk = false; }
        string testInput = null;
        try { testInput = Environment.GetEnvironmentVariable("PCLPATCH_TEST_INPUT"); } catch { }
        if (!string.IsNullOrEmpty(testInput)) canAsk = true;
        if (files.Count == 0)
        {
            Usage();
            Console.WriteLine();
            Console.Write(">> \u8bf7\u8f93\u5165 PCL \u542f\u52a8\u5668 exe \u7684\u5b8c\u6574\u8def\u5f84\uff08\u56de\u8f66\u7ed3\u675f\uff09\uff1a");
            string line = Console.ReadLine();
            if (!string.IsNullOrEmpty(line)) files.Add(line.Trim().Trim('"'));
        }
        if (files.Count == 0) { Say("\u6ca1\u6709\u5f85\u5904\u7406\u7684\u6587\u4ef6\u3002"); Pause(); return 1; }
        if (outPath != null && files.Count > 1) { Say("[\u9519\u8bef] --out \u53ea\u80fd\u914d\u5408\u5355\u4e2a\u6587\u4ef6\u4f7f\u7528\u3002"); return 2; }

        if (!modeExplicit && !canAsk)
            Say("  \u63d0\u793a\uff1a\u975e\u4ea4\u4e92\u73af\u5883\uff0c\u9ed8\u8ba4\u6267\u884c\u201c\u89e3\u9501\u4e0b\u8f7d\u201d\uff08\u53ef\u7528 --block-update / --uninstall \u6307\u5b9a\uff09");
        foreach (string g in files) ProcessOne(g, modeExplicit, blockUpdate, uninstall, dryRun, noBackup, outPath, canAsk, testInput);

        Say("");
        Say(_errors == 0 ? "\u5168\u90e8\u5b8c\u6210\u3002" : "\u5b8c\u6210\uff0c\u4f46\u6709 " + _errors + " \u4e2a\u9519\u8bef\u3002");
        Pause();
        return _errors == 0 ? 0 : 3;
    }

    static void OnUnhandled(object sender, UnhandledExceptionEventArgs e)
    {
        try
        {
            Exception ex = e.ExceptionObject as Exception;
            string msg = (ex != null) ? SafeErr(ex) : "\u975e\u5f02\u5e38\u5d29\u6e83\uff08" + e.ExceptionObject + "\uff09";
            Say("");
            Say("[\u5d29\u6e83] \u672a\u6355\u83b7\u7684\u5f02\u5e38\uff1a" + msg);
            try
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                string log = Path.Combine(dir, "PclUnlockPatcher.error.log");
                File.AppendAllText(log, DateTime.Now.ToString("s") + "  " + msg + "\r\n");
                Say("[\u5d29\u6e83] \u5df2\u5199\u5165\u65e5\u5fd7\uff1a" + log);
            }
            catch { }
            Say("\u8bf7\u628a\u4e0a\u9762\u7684\u4fe1\u606f\u53cd\u9988\u7ed9\u5de5\u5177\u4f5c\u8005\u3002");
        }
        catch { }
    }

    static bool TryWriteBytes(string file, byte[] bytes, string what, string extraHint)
    {
        try { File.WriteAllBytes(file, bytes); return true; }
        catch (Exception ex)
        {
            Say("  [\u9519\u8bef] " + what + "\u5931\u8d25\uff1a" + file);
            Say("        \u539f\u56e0\uff1a" + SafeErr(ex));
            if (!string.IsNullOrEmpty(extraHint)) Say("        " + extraHint);
            return false;
        }
    }

    static void InstallResolver()
    {
        AppDomain.CurrentDomain.AssemblyResolve += OnResolve;
    }

    static System.Reflection.Assembly OnResolve(object sender, ResolveEventArgs e)
    {
        try
        {
            if (e.Name == null || e.Name.IndexOf("Mono.Cecil", StringComparison.OrdinalIgnoreCase) < 0) return null;
            using (Stream s = typeof(Program).Assembly.GetManifestResourceStream("Mono.Cecil.dll"))
            {
                if (s == null) return null;
                var buf = new byte[s.Length];
                int read = 0;
                while (read < buf.Length)
                {
                    int n = s.Read(buf, read, buf.Length - read);
                    if (n <= 0) break;
                    read += n;
                }
                return System.Reflection.Assembly.Load(buf);
            }
        }
        catch { return null; }
    }

    static int AskChoice(string injected)
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            string line;
            if (injected != null)
            {
                line = injected;
                Say(">> \u5df2\u6ce8\u5165\u9009\u62e9\uff1a" + line);
            }
            else
            {
                Console.Write(">> \u8bf7\u8f93\u5165 1 / 2 / 3 \u540e\u56de\u8f66\uff08\u76f4\u63a5\u56de\u8f66\u53d6\u6d88\uff09\uff1a");
                try { line = Console.ReadLine(); } catch { return 0; }
            }
            if (line == null) return 0;
            line = line.Trim();
            if (line.Length == 0) return 0;
            if (line == "1") return 1;
            if (line == "2") return 2;
            if (line == "3") return 3;
            if (injected != null) { Say("  \u8f93\u5165\u65e0\u6548\uff1a" + line); return 0; }
            Say("  \u8f93\u5165\u65e0\u6548\uff0c\u8bf7\u8f93\u5165 1\u30012 \u6216 3\u3002");
        }
        return 0;
    }

    static bool DetectUnlocked(ModuleDefinition mod)
    {
        MethodDefinition init = FindMethod(mod.GetType(InstallPage), InstallInit);
        if (init == null || !init.HasBody) return false;
        bool hasLdsfld = false, hasDummy = false;
        foreach (Instruction i in init.Body.Instructions)
        {
            if (i.OpCode.Code == Code.Ldsfld && RefName(i) != null && RefName(i).Contains(DlLoaderType + "::" + DlLoaderField)) hasLdsfld = true;
            if (i.OpCode.Code == Code.Ldstr && i.Operand is string && (string)i.Operand == DummyTaskName) hasDummy = true;
        }
        return hasLdsfld && !hasDummy;
    }

    static bool DetectBlocked(ModuleDefinition mod)
    {
        MethodDefinition pm = FindMethod(mod.GetType(SecretType), PermGetter);
        if (pm == null || !pm.HasBody) return false;
        var ins = pm.Body.Instructions;
        return ins.Count >= 2 && ins[0].OpCode.Code == Code.Ldc_I4_0 && ins[1].OpCode.Code == Code.Ret;
    }

    static bool BytesEqual(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    static void DoUninstall(string path, string bakPath, byte[] current, bool noBackup)
    {
        Say("");
        if (!File.Exists(bakPath))
        {
            Say("  [\u9519\u8bef] \u672a\u627e\u5230\u5907\u4efd\uff1a" + bakPath);
            Say("        \u65e0\u6cd5\u5378\u9664\u8865\u4e01\uff1a\u8865\u4e01\u662f\u7b49\u957f\u5b57\u8282\u6539\u5199\uff0c\u6ca1\u6709\u539f\u59cb\u5b57\u8282\u5c31\u65e0\u6cd5\u53ef\u9760\u8fd8\u539f\u3002");
            Say("        \u8bf7\u91cd\u65b0\u4e0b\u8f7d\u5b98\u65b9\u542f\u52a8\u5668\uff0c\u6216\u4ece\u5176\u4ed6\u4f4d\u7f6e\u7684\u5907\u4efd\u6062\u590d\u3002");
            _errors++;
            return;
        }
        byte[] bak;
        try { bak = File.ReadAllBytes(bakPath); }
        catch (Exception ex) { Say("  [\u9519\u8bef] \u65e0\u6cd5\u8bfb\u53d6\u5907\u4efd\uff1a" + SafeErr(ex)); _errors++; return; }

        if (BytesEqual(bak, current)) { Say("  \u5f53\u524d\u6587\u4ef6\u4e0e\u5907\u4efd\u5b8c\u5168\u4e00\u81f4\uff0c\u5df2\u662f\u539f\u7248\uff0c\u65e0\u9700\u5378\u9664\u3002"); return; }

        string bakName = null;
        try
        {
            var ba = AssemblyDefinition.ReadAssembly(new MemoryStream(bak), new ReaderParameters { ReadingMode = ReadingMode.Immediate });
            bakName = ba.Name.Name;
            ba.Dispose();
        }
        catch (Exception ex) { Say("  [\u9519\u8bef] \u5907\u4efd\u4e0d\u662f\u6709\u6548\u7684 .NET \u7a0b\u5e8f\uff1a" + SafeErr(ex)); _errors++; return; }

        Say("  \u5c06\u7528\u5907\u4efd\u8fd8\u539f\uff1a" + bakPath);
        Say("  \u5907\u4efd\u7a0b\u5e8f\u96c6\uff1a" + bakName + "\uff0c\u5927\u5c0f\uff1a" + bak.Length);

        string dir = Path.GetDirectoryName(Path.GetFullPath(path));
        string probe = Path.Combine(dir, ".pclpatch_wtest.tmp");
        try { File.WriteAllBytes(probe, new byte[] { 0 }); File.Delete(probe); }
        catch (Exception ex)
        {
            Say("  [\u9519\u8bef] \u76ee\u6807\u76ee\u5f55\u4e0d\u53ef\u5199\uff1a" + dir);
            Say("        \u539f\u56e0\uff1a" + SafeErr(ex));
            SayWriteContextDiagnosis(Path.GetDirectoryName(Path.GetFullPath(path)));
            _errors++;
            return;
        }
        try
        {
            FileAttributes at = File.GetAttributes(path);
            if ((at & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(path, at & ~FileAttributes.ReadOnly);
                Say("  \u5df2\u6e05\u9664\u76ee\u6807\u6587\u4ef6\u7684\u53ea\u8bfb\u5c5e\u6027\u3002");
            }
        }
        catch { }

        if (!TryWriteBytes(path, bak, "\u8fd8\u539f", "\u8bf7\u5148\u5b8c\u5168\u9000\u51fa PCL \u540e\u91cd\u8bd5\u3002")) { _errors++; return; }
        Say("  \u5df2\u8fd8\u539f\uff1a" + path);
        Say("  \u65b0 SHA256\uff1a" + Sha256(bak));
        Say("  \u5907\u4efd\u6587\u4ef6\u4fdd\u7559\uff1b\u9700\u8981\u91cd\u65b0\u89e3\u9501\u65f6\u518d\u8fd0\u884c\u672c\u5de5\u5177\u5373\u53ef\u3002");
    }


    static void SayWriteContextDiagnosis(string outDir)
    {
        string env = null;
        try
        {
            foreach (System.Collections.DictionaryEntry kv in Environment.GetEnvironmentVariables())
            {
                string k = kv.Key as string;
                if (!string.IsNullOrEmpty(k) && k.StartsWith("DSH_", StringComparison.OrdinalIgnoreCase)) { env = k; break; }
            }
        }
        catch { }

        bool tempOk = false;
        try
        {
            string tp = Path.Combine(Path.GetTempPath(), "pclpatch_probe.tmp");
            File.WriteAllBytes(tp, new byte[] { 0 });
            File.Delete(tp);
            tempOk = true;
        }
        catch { tempOk = false; }

        Say("  [\u8bca\u65ad] \u5f53\u524d\u7528\u6237：" + SafeUser());
        if (env != null)
        {
            Say("  [\u8bca\u65ad] \u68c0\u6d4b\u5230\u73af\u5883\u53d8\u91cf " + env + " \uff1a\u5f53\u524d\u8fdb\u7a0b\u8fd0\u884c\u5728 DSH \u6c99\u7bb1/\u53d7\u9650\u73af\u5883\u4e2d\uff0c");
            Say("         \u5b83\u53ea\u88ab\u5141\u8bb8\u5199\u5165\u5de5\u4f5c\u533a\uff0c\u5199\u522b\u7684\u76ee\u5f55\u4e00\u5f8b\u88ab\u62d2\u3002\u8fd9\u4e0e\u76ee\u5f55\u672c\u8eab\u7684\u6743\u9650\u65e0\u5173\u3002");
            Say("         \u8bf7\u7528 Windows \u8d44\u6e90\u7ba1\u7406\u5668\u76f4\u63a5\u53cc\u51fb PclUnlockPatcher.exe\uff08\u4e0d\u8981\u901a\u8fc7 DSH \u542f\u52a8\uff09\uff0c");
            Say("         \u518d\u628a PCL \u7684 exe \u62d6\u8fdb\u53bb\u5373\u53ef\u3002");
        }
        else if (!tempOk)
        {
            Say("  [\u8bca\u65ad] \u8fde\u7528\u6237\u4e34\u65f6\u76ee\u5f55\u90fd\u4e0d\u53ef\u5199\uff1a\u5f53\u524d\u8fdb\u7a0b\u6743\u9650\u53d7\u9650\uff08\u6c99\u7bb1 / \u4f4e\u5b8c\u6574\u6027 / \u53ea\u8bfb\u6a21\u5f0f\uff09\u3002");
            Say("         \u8bf7\u6539\u7528\u8d44\u6e90\u7ba1\u7406\u5668\u76f4\u63a5\u53cc\u51fb\u8fd0\u884c\u672c\u5de5\u5177\uff0c\u6216\u6362\u4e00\u4e2a\u666e\u901a\u547d\u4ee4\u884c\u7a97\u53e3\u8fd0\u884c\u3002");
        }
        else
        {
            Say("  [\u8bca\u65ad] \u4e34\u65f6\u76ee\u5f55\u53ef\u5199\u3001\u4ec5\u8be5\u76ee\u5f55\u4e0d\u53ef\u5199\uff1a\u5c5e\u4e8e\u76ee\u6807\u76ee\u5f55\u7684\u6743\u9650\u6216\u5b89\u5168\u7b56\u7565\u95ee\u9898\u3002");
            Say("         \u53ef\u5148\u5728\u8be5\u6587\u4ef6\u5939\u91cc\u624b\u52a8\u65b0\u5efa\u4e00\u4e2a\u6587\u672c\u6587\u4ef6\uff1a\u82e5\u4e5f\u5931\u8d25\uff0c\u8bf4\u660e\u76ee\u5f55\u6743\u9650\u53d7\u9650\uff1b");
            Say("         \u53ef\u53f3\u952e\u8be5\u6587\u4ef6\u5939 \u2192 \u5c5e\u6027 \u2192 \u5b89\u5168\uff0c\u4e3a\u5f53\u524d\u7528\u6237\u6dfb\u52a0\u201c\u5b8c\u5168\u63a7\u5236\u201d\uff0c\u6216\u4ee5\u7ba1\u7406\u5458\u8eab\u4efd\u8fd0\u884c\u672c\u5de5\u5177\u3002");
        }
        Say("         \u76ee\u6807\u76ee\u5f55：" + outDir);
    }

    static string SafeUser()
    {
        try { return System.Security.Principal.WindowsIdentity.GetCurrent().Name; } catch { }
        try { return Environment.UserDomainName + "\\" + Environment.UserName; } catch { }
        return "?";
    }

    static void Pause()
    {
        if (!Environment.UserInteractive) return;
        try { Console.WriteLine(); Console.Write("\u6309\u4efb\u610f\u952e\u9000\u51fa\u3002\u3002\u3002"); Console.ReadKey(true); }
        catch { }
    }

    static void Usage()
    {
        Say("");
        Say("\u7528\u6cd5\uff1a\u628a PCL \u7684 exe \u76f4\u63a5\u62d6\u5230\u672c\u7a0b\u5e8f\u4e0a\uff0c\u4f1a\u51fa\u73b0\u83dc\u5355\u8ba9\u4f60\u9009\uff1a");
        Say("    1. \u89e3\u9501\u4e0b\u8f7d\uff08\u4fdd\u7559\u66f4\u65b0\u529f\u80fd\uff09");
        Say("    2. \u89e3\u9501\u4e0b\u8f7d\u5e76\u5c4f\u853d\u66f4\u65b0");
        Say("    3. \u5378\u9664\u8865\u4e01\uff08\u7528\u5907\u4efd\u8fd8\u539f\u4e3a\u539f\u7248\uff09");
        Say("\u4e5f\u53ef\u4ee5\u547d\u4ee4\u884c\u76f4\u63a5\u6307\u5b9a\u6a21\u5f0f\uff08\u4e0d\u5f39\u83dc\u5355\uff09\uff1a");
        Say("  PclUnlockPatcher.exe \"D:\\PCL2\\Plain Craft Launcher 2.exe\" --unlock-only | --block-update | --uninstall");
        Say("\u5176\u5b83\uff1a--dry-run \u53ea\u68c0\u6d4b\u4e0d\u5199\u5165\uff1b--no-backup \u4e0d\u751f\u6210 .bak\uff1b--out <\u8def\u5f84> \u8f93\u51fa\u5230\u53e6\u4e00\u4e2a\u6587\u4ef6");
    }

    static void Say(string s) { Console.WriteLine(s); }

    static string SafeErr(Exception ex)
    {
        string t = "?", m = "", st = "";
        try { t = ex.GetType().FullName; } catch { }
        try { m = ex.Message; } catch { }
        try { st = ex.StackTrace; } catch { }
        var sb = new StringBuilder();
        sb.Append(t).Append(": ").Append(m ?? "");
        if (!string.IsNullOrEmpty(st)) sb.Append("  @ ").Append(st.Split('\n')[0].Trim());
        try
        {
            if (ex.InnerException != null)
                sb.Append("  [inner ").Append(ex.InnerException.GetType().Name).Append(": ").Append(ex.InnerException.Message).Append("]");
        }
        catch { }
        return sb.ToString();
    }

    static void ProcessOne(string path, bool modeExplicit, bool blockUpdate, bool uninstallWanted, bool dryRun, bool noBackup, string outPath, bool canAsk, string injectedInput)
    {
        bool disableUpdate = blockUpdate;
        Say("");
        Say("----------------------------------------------");
        Say("\u6587\u4ef6\uff1a" + path);
        if (!File.Exists(path)) { Say("[\u9519\u8bef] \u6587\u4ef6\u4e0d\u5b58\u5728\u3002"); _errors++; return; }

        byte[] data;
        try
        {
            data = File.ReadAllBytes(path);
        }
        catch (Exception ex)
        {
            Say("[\u9519\u8bef] \u65e0\u6cd5\u8bfb\u53d6\u8be5\u6587\u4ef6\uff1a" + path);
            Say("        \u539f\u56e0\uff1a" + SafeErr(ex));
            SayWriteContextDiagnosis(Path.GetDirectoryName(Path.GetFullPath(path)));
            Say("        \u8be5\u6587\u4ef6\u53ef\u80fd\u6b63\u88ab PCL \u6216\u5176\u4ed6\u7a0b\u5e8f\u5360\u7528\uff0c\u8bf7\u5148\u5b8c\u5168\u9000\u51fa\u542f\u52a8\u5668\u540e\u91cd\u8bd5\uff1b");
            Say("        \u6216\u628a\u8be5 exe \u590d\u5236\u5230\u684c\u9762\u5904\u7406\uff0c\u5b8c\u6210\u540e\u518d\u62f7\u56de\u539f\u4f4d\u7f6e\u3002");
            _errors++;
            return;
        }
        Say("\u539f\u59cb SHA256\uff1a" + Sha256(data) + "   \u5927\u5c0f\uff1a" + data.Length);


        // v1.1.0 \u521d\u7248\u5199\u5165\u7684\u5e03\u5c40\u65e0\u6cd5\u88ab\u7cfb\u7edf\u52a0\u8f7d\u5668\u6620\u5c04\uff0c\u8fd9\u91cc\u7528 .bak \u81ea\u52a8\u91cd\u5efa
        if (!dryRun && IsKnownBroken(Sha256(data)))
        {
            string bkp = path + ".bak";
            if (File.Exists(bkp))
            {
                Say("  [" + "\u4fee\u590d" + "] " + "\u8be5\u6587\u4ef6\u662f v1.1.0 \u521d\u7248\u5199\u5165\u7684\u4e0d\u53ef\u542f\u52a8\u5e03\u5c40\uff0c\u5df2\u6539\u7528 .bak \u4f5c\u4e3a\u57fa\u51c6\u91cd\u65b0\u6ce8\u5165\u3002");
                data = File.ReadAllBytes(bkp);
                Say("  \u5907\u4efd SHA256\uff1a" + Sha256(data) + "   \u5927\u5c0f\uff1a" + data.Length);
            }
            else
            {
                Say("  [" + "\u9519\u8bef" + "] " + "\u8be5\u6587\u4ef6\u662f\u65e7\u7248\u5199\u5165\u7684\u4e0d\u53ef\u542f\u52a8\u5e03\u5c40\uff0c\u4e14\u6ca1\u6709 .bak \u5907\u4efd\uff1b\u8bf7\u91cd\u65b0\u4e0b\u8f7d\u5b98\u65b9\u542f\u52a8\u5668\u540e\u518d\u5904\u7406\u3002");
                _errors++;
                return;
            }
        }
        AssemblyDefinition asm;
        try { asm = AssemblyDefinition.ReadAssembly(new MemoryStream(data), new ReaderParameters { ReadingMode = ReadingMode.Immediate }); }
        catch (Exception ex) { Say("[\u9519\u8bef] \u4e0d\u662f\u6709\u6548\u7684 .NET \u7a0b\u5e8f\uff1a" + SafeErr(ex)); _errors++; return; }

        ModuleDefinition mod = asm.MainModule;
        Say("\u7a0b\u5e8f\u96c6\uff1a" + asm.Name.Name + "  \u7248\u672c\uff1a" + asm.Name.Version);
        string vb = ConstValue(mod, "PCL.ModBase", "VersionBaseName");
        string vc = ConstValue(mod, "PCL.ModBase", "VersionCode");
        if (vb != null || vc != null) Say("\u6807\u8bc6\uff1a" + (vb ?? "?") + " (code " + (vc ?? "?") + ")");
        if (asm.Name.Name.IndexOf("Plain Craft Launcher", StringComparison.OrdinalIgnoreCase) < 0)
            Say("[\u6ce8\u610f] \u7a0b\u5e8f\u96c6\u540d\u4e0d\u50cf PCL\uff0c\u5c06\u7ee7\u7eed\u5c1d\u8bd5\u3002");

        string lastSec = PackRestorer.LastSectionName(data);
        if (!dryRun && lastSec != null && lastSec.StartsWith(".pclx") && lastSec != ".pclx3")
        {
            string bkp2 = path + ".bak";
            if (File.Exists(bkp2))
            {
                Say("  [" + "\\u5347\\u7ea7" + "] " + "\\u8be5\\u6587\\u4ef6\\u7531\\u65e7\\u7248\\u672c\\u5de5\\u5177\\u5904\\u7406\\u8fc7\\uff0c\\u5df2\\u6539\\u7528 .bak \\u4f5c\\u4e3a\\u57fa\\u51c6\\u91cd\\u65b0\\u6ce8\\u5165\\u3002");
                data = File.ReadAllBytes(bkp2);
                try { asm.Dispose(); } catch { }
                asm = AssemblyDefinition.ReadAssembly(new MemoryStream(data), new ReaderParameters { ReadingMode = ReadingMode.Immediate });
                mod = asm.MainModule;
                Say("  \u57fa\u51c6 SHA256\uff1a" + Sha256(data) + "   \u5927\u5c0f\uff1a" + data.Length);
            }
        }
        bool isUnlocked = DetectUnlocked(mod);
        bool isBlocked = DetectBlocked(mod);
        string bakPath = path + ".bak";
        bool hasBak = File.Exists(bakPath);
        Say("  \u5f53\u524d\u72b6\u6001\uff1a" + (isUnlocked ? "\u5df2\u89e3\u9501\u4e0b\u8f7d" : "\u672a\u89e3\u9501") +
            " / " + (isBlocked ? "\u5df2\u5c4f\u853d\u66f4\u65b0" : "\u672a\u5c4f\u853d\u66f4\u65b0") +
            " / " + (hasBak ? "\u5b58\u5728\u5907\u4efd" : "\u65e0\u5907\u4efd"));

        if (!modeExplicit && canAsk)
        {
            Say("");
            Say("  \u8bf7\u9009\u62e9\u8981\u6267\u884c\u7684\u64cd\u4f5c\uff1a");
            Say("    1. \u89e3\u9501\u4e0b\u8f7d\uff08\u4fdd\u7559\u66f4\u65b0\u529f\u80fd\uff09");
            Say("    2. \u89e3\u9501\u4e0b\u8f7d\u5e76\u5c4f\u853d\u66f4\u65b0");
            Say("    3. \u5378\u9664\u8865\u4e01\uff08\u7528\u5907\u4efd\u8fd8\u539f\u4e3a\u539f\u7248\uff09");
            Say("--------------------------------------------------------------");
            int choice = AskChoice(injectedInput);
            if (choice == 0) { Say("  \u5df2\u53d6\u6d88\uff0c\u672a\u4fee\u6539\u4efb\u4f55\u6587\u4ef6\u3002"); asm.Dispose(); return; }
            if (choice == 3) uninstallWanted = true;
            else disableUpdate = (choice == 2);
            Say("");
        }

        if (uninstallWanted)
        {
            DoUninstall(path, bakPath, data, noBackup);
            asm.Dispose();
            return;
        }

        var pe = new PeMap(data);
        var edits = new List<Edit>();
        var report = new List<string>();
        bool unlockApplied = false, unlockAlready = false, noRestriction = false;

        // 1. 下载解锁
        try
        {
            TypeDefinition page = mod.GetType(InstallPage);
            MethodDefinition init = FindMethod(page, InstallInit);
            FieldDefinition dlField = FindField(mod.GetType(DlLoaderType), DlLoaderField);
            bool anyRestrict = RestrictionLambdas(mod).Any();

            if (page == null) report.Add("\u4e0b\u8f7d\u89e3\u9501\uff1a\u672a\u627e\u5230 " + InstallPage);
            else if (init == null) report.Add("\u4e0b\u8f7d\u89e3\u9501\uff1a\u672a\u627e\u5230 " + InstallInit);
            else if (dlField == null) report.Add("\u4e0b\u8f7d\u89e3\u9501\uff1a\u672a\u627e\u5230 " + DlLoaderType + "." + DlLoaderField);
            else
            {
                string why;
                int n = PlanUnlock(pe, init, dlField, anyRestrict, edits, out why, ref unlockAlready, ref noRestriction);
                if (n > 0) { unlockApplied = true; report.Add("\u4e0b\u8f7d\u89e3\u9501\uff1a\u6210\u529f\uff08" + why + "\uff09"); }
                else if (unlockAlready && noRestriction) report.Add("\u4e0b\u8f7d\u89e3\u9501\uff1a\u8be5\u7248\u672c\u672c\u8eab\u6ca1\u6709\u4e0b\u8f7d\u9650\u5236\uff0c\u65e0\u9700\u6ce8\u5165");
                else if (unlockAlready) report.Add("\u4e0b\u8f7d\u89e3\u9501\uff1a\u5df2\u662f\u89e3\u9501\u72b6\u6001\uff0c\u65e0\u9700\u91cd\u590d\u6ce8\u5165");
                else report.Add("\u4e0b\u8f7d\u89e3\u9501\uff1a\u672a\u627e\u5230\u53ef\u6ce8\u5165\u7684\u9650\u5236\u5206\u652f\uff08" + why + "\uff09");
            }
        }
        catch (Exception ex) { report.Add("\u4e0b\u8f7d\u89e3\u9501\uff1a\u5f02\u5e38 " + SafeErr(ex)); }

        // 2. 更新屏蔽
        if (disableUpdate)
        {
            try
            {
                TypeDefinition secret = mod.GetType(SecretType);
                if (secret == null) report.Add("\u66f4\u65b0\u5c4f\u853d\uff1a\u672a\u627e\u5230 " + SecretType + "\uff0c\u8df3\u8fc7");
                else
                {
                    MethodDefinition perm = FindMethod(secret, PermGetter);
                    bool permAlready = perm != null && perm.HasBody && perm.Body.Instructions.Count >= 2
                                       && perm.Body.Instructions[0].OpCode.Code == Code.Ldc_I4_0
                                       && perm.Body.Instructions[1].OpCode.Code == Code.Ret;
                    if (permAlready) report.Add("\u66f4\u65b0\u5c4f\u853d\uff1a" + PermGetter + " \u5df2\u662f\u6052 False\uff0c\u65e0\u9700\u91cd\u590d");
                    else if (perm != null && perm.HasBody && perm.Body.ExceptionHandlers.Count == 0 && perm.Body.CodeSize >= 2)
                    {
                        var bytes = new byte[perm.Body.CodeSize];
                        bytes[0] = 0x16; bytes[1] = 0x2A;
                        edits.Add(new Edit(pe.IlOffset(perm, 0), bytes, "HasUpdatePermission \u2192 \u6052 False"));
                        report.Add("\u66f4\u65b0\u5c4f\u853d\uff1a" + PermGetter + " \u2192 \u6052\u8fd4\u56de False");
                    }
                    else report.Add("\u66f4\u65b0\u5c4f\u853d\uff1a" + PermGetter + " \u4e0d\u53ef\u7528\uff0c\u8df3\u8fc7");

                    int a = PlanNopCalls(pe, mod, SecretType, CheckMethod, edits);
                    int b = PlanDropCalls(pe, mod, SecretType, ReplaceMethod, edits);
                    report.Add("\u66f4\u65b0\u5c4f\u853d\uff1a\u201c\u68c0\u67e5\u66f4\u65b0\u201d\u5165\u53e3 " + a + " \u5904\uff0c\u201c\u81ea\u6211\u66ff\u6362\u201d " + b + " \u5904");
                }
            }
            catch (Exception ex) { report.Add("\u66f4\u65b0\u5c4f\u853d\uff1a\u5f02\u5e38 " + SafeErr(ex)); }
        }
        else
        {
            MethodDefinition pm = FindMethod(mod.GetType(SecretType), PermGetter);
            bool blocked = pm != null && pm.HasBody && pm.Body.Instructions.Count >= 2
                           && pm.Body.Instructions[0].OpCode.Code == Code.Ldc_I4_0
                           && pm.Body.Instructions[1].OpCode.Code == Code.Ret;
            report.Add(blocked
                ? "\u66f4\u65b0\uff1a\u8be5\u6587\u4ef6\u6b64\u524d\u5df2\u5c4f\u853d\u66f4\u65b0\uff08\u672c\u6b21\u672a\u6539\u52a8\uff09"
                : "\u66f4\u65b0\uff1a\u672a\u5c4f\u853d\uff08\u4ec5\u89e3\u9501\u4e0b\u8f7d\uff09\uff1b\u82e5\u542f\u52a8\u5668\u81ea\u52a8\u66f4\u65b0\u5230\u5b98\u65b9\u65b0\u7248\uff0c\u89e3\u9501\u4f1a\u88ab\u8986\u76d6\uff0c\u5c4a\u65f6\u91cd\u65b0\u8fd0\u884c\u672c\u5de5\u5177\u5373\u53ef");
        }

        // 3. 限制界面（修改页版本卡片）
        try
        {
            int vis = PlanVisibility(pe, mod, edits);
            report.Add(vis > 0
                ? "\u9650\u5236\u754c\u9762\uff1a\u4fee\u6539\u9875\u7248\u672c\u5361\u7247\u5df2\u6539\u4e3a\u53ef\u89c1 " + vis + " \u5904"
                : "\u9650\u5236\u754c\u9762\uff1a\u672a\u627e\u5230\u7248\u672c\u5361\u7247\u5185\u7f16\u7801\uff0c\u8df3\u8fc7");
        }
        catch (Exception ex) { report.Add("\u9650\u5236\u754c\u9762\uff1a\u5f02\u5e38 " + SafeErr(ex)); }


        // 4. \u6574\u5408\u5305\u5b89\u88c5\u6062\u590d
        PackJob packJob = null;
        try
        {
            string packWhy;
            packJob = PackRestorer.Plan(data, mod, RestrictText, out packWhy);
            if (packJob != null) report.Add("\u6574\u5408\u5305\u5b89\u88c5\uff1a\u5df2\u6062\u590d\uff08" + packJob.Detail + "\uff09");
            else report.Add("\u6574\u5408\u5305\u5b89\u88c5\uff1a" + (packWhy == null ? "\u65e0\u9700\u5904\u7406" : packWhy));
        }
        catch (Exception ex) { report.Add("\u6574\u5408\u5305\u5b89\u88c5\uff1a\u5f02\u5e38 " + SafeErr(ex)); }


        // 3.5 \u8ba9\u6574\u5408\u5305\u5b89\u88c5\u5931\u8d25\u65f6\u7684\u771f\u5b9e\u9519\u8bef\u663e\u5f62
        try
        {
            int real = PlanShowRealMessage(pe, mod, edits);
            report.Add(real > 0 ? "\u9519\u8bef\u53ef\u89c1\u6027\uff1a\u6574\u5408\u5305\u5b89\u88c5\u5931\u8d25\u65f6\u4f1a\u76f4\u63a5\u63d0\u793a\u771f\u5b9e\u539f\u56e0\uff08" + real + " \u5904\uff09" : "\u9519\u8bef\u53ef\u89c1\u6027\uff1a\u672a\u627e\u5230\u5bf9\u5e94\u5224\u5b9a\uff0c\u8df3\u8fc7");
        }
        catch (Exception ex) { report.Add("\u9519\u8bef\u53ef\u89c1\u6027\uff1a\u5f02\u5e38 " + SafeErr(ex)); }
        asm.Dispose();
        foreach (string r in report) Say("  \u00b7 " + r);

        if (edits.Count == 0 && packJob == null) { Say("  [\u8df3\u8fc7] \u6ca1\u6709\u9700\u8981\u4fee\u6539\u7684\u5185\u5bb9\uff0c\u4e0d\u5199\u5165\u3002"); return; }
        if (!CheckEdits(data, edits)) { Say("  [\u9519\u8bef] \u4fee\u6539\u533a\u57df\u6709\u91cd\u53e0\u6216\u8d85\u754c\uff0c\u4e2d\u6b62\u3002"); _errors++; return; }

        try
        {
            FileAttributes attrs = File.GetAttributes(path);
            if ((attrs & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
                Say("  \u6ce8\u610f\uff1a\u76ee\u6807\u6587\u4ef6\u5e26\u53ea\u8bfb\u5c5e\u6027\uff0c\u5df2\u81ea\u52a8\u6e05\u9664\u3002");
            }
        }
        catch { }

        string outDir = Path.GetDirectoryName(Path.GetFullPath(outPath ?? path));
        string probe = Path.Combine(outDir, ".pclpatch_wtest.tmp");
        try
        {
            File.WriteAllBytes(probe, new byte[] { 0x00 });
            File.Delete(probe);
        }
        catch (Exception ex)
        {
            Say("  [\u9519\u8bef] \u76ee\u6807\u76ee\u5f55\u4e0d\u53ef\u5199\uff1a" + outDir);
            Say("        \u539f\u56e0\uff1a" + SafeErr(ex));
            SayWriteContextDiagnosis(Path.GetDirectoryName(Path.GetFullPath(path)));
            Say("        \u8bf7\u628a exe \u590d\u5236\u5230\u684c\u9762\u7b49\u53ef\u5199\u76ee\u5f55\u5904\u7406\uff0c\u5b8c\u6210\u540e\u518d\u62f7\u56de\u539f\u4f4d\u7f6e\uff1b");
            Say("        \u5982\u679c\u662f\u901a\u8fc7\u6c99\u7bb1/\u9650\u5236\u73af\u5883\u8fd0\u884c\u672c\u5de5\u5177\uff0c\u8bf7\u6539\u7528\u8d44\u6e90\u7ba1\u7406\u5668\u76f4\u63a5\u53cc\u51fb\u8fd0\u884c\uff1b");
            Say("        \u4e5f\u53ef\u5173\u95ed\u6b63\u5728\u8fd0\u884c\u7684 PCL\uff0c\u6216\u4ee5\u7ba1\u7406\u5458\u8eab\u4efd\u8fd0\u884c\u672c\u5de5\u5177\u3002");
            _errors++;
            return;
        }

        var patched = (byte[])data.Clone();
        foreach (Edit e in edits) Array.Copy(e.Bytes, 0, patched, e.Offset, e.Bytes.Length);
        if (packJob != null) patched = packJob.Apply(patched);
        Say("  \u4fee\u6539\u70b9\uff1a" + edits.Count + " \u5904\uff0c\u5171 " + edits.Sum(x => x.Bytes.Length) + " \u5b57\u8282\uff08\u7b49\u957f\u66ff\u6362\uff09");
        foreach (Edit e in edits) Say("    0x" + e.Offset.ToString("X") + "  " + e.Bytes.Length + "B  " + e.Why);
        if (packJob != null) Say("  \u6574\u5408\u5305\u5b89\u88c5\uff1a\u6269\u5c55\u6bb5 .pclx\uff0c\u6587\u4ef6\u4ece " + data.Length + " \u5b57\u8282\u53d8\u4e3a " + (packJob.RawPtr + packJob.RawSize) + " \u5b57\u8282\uff08\u65b9\u6cd5 RVA 0x" + packJob.NewMethodRva.ToString("X") + "\uff09");

        if (dryRun) { Say("\u8bd5\u8fd0\u884c\uff1a\u672a\u5199\u5165\u4efb\u4f55\u6587\u4ef6\u3002"); return; }

        string target = outPath ?? path;
        if (outPath == null && !noBackup)
        {
            string bak = path + ".bak";
            if (!File.Exists(bak))
            {
                Say("  \u6b63\u5728\u751f\u6210\u5907\u4efd\u2026");
                if (!TryWriteBytes(bak, data, "\u751f\u6210\u5907\u4efd", "\u539f\u6587\u4ef6\u672a\u88ab\u4fee\u6539\uff0c\u53ef\u5b89\u5168\u653e\u5fc3\u3002")) { _errors++; return; }
                Say("  \u5df2\u5907\u4efd\uff1a" + bak);
            }
            else Say("  \u5df2\u5b58\u5728\u5907\u4efd\uff0c\u4fdd\u7559\uff1a" + bak);
        }
        Say("  \u6b63\u5728\u5199\u5165\u2026");
        if (!TryWriteBytes(target, patched, "\u5199\u5165",
            "\u8be5\u6587\u4ef6\u53ef\u80fd\u6b63\u88ab PCL \u5360\u7528\uff0c\u8bf7\u5148\u5b8c\u5168\u9000\u51fa\u542f\u52a8\u5668\u540e\u91cd\u8bd5\u3002")) { _errors++; return; }
        Say("  \u5df2\u5199\u5165\uff1a" + target);
        Say("  \u65b0 SHA256\uff1a" + Sha256(patched));

        try
        {
            var chk = AssemblyDefinition.ReadAssembly(new MemoryStream(patched), new ReaderParameters { ReadingMode = ReadingMode.Immediate });
            string v = Verify(chk.MainModule, unlockApplied || unlockAlready, disableUpdate, packJob != null);
            chk.Dispose();
            Say("  \u56de\u8bfb\u6821\u9a8c\uff1a" + v);
            if (v.StartsWith("FAIL")) _errors++;
        }
        catch (Exception ex) { Say("  [\u9519\u8bef] \u56de\u8bfb\u6821\u9a8c\u5f02\u5e38\uff1a" + SafeErr(ex)); _errors++; }
    }

    static bool CheckEdits(byte[] data, List<Edit> edits)
    {
        var sorted = edits.OrderBy(e => e.Offset).ToList();
        for (int i = 0; i < sorted.Count; i++)
        {
            if (sorted[i].Offset < 0 || sorted[i].Offset + sorted[i].Bytes.Length > data.Length) return false;
            if (i > 0 && sorted[i - 1].Offset + sorted[i - 1].Bytes.Length > sorted[i].Offset) return false;
        }
        return true;
    }

    static int PlanUnlock(PeMap pe, MethodDefinition init, FieldDefinition dlField, bool anyRestrict,
                          List<Edit> edits, out string detail, ref bool alreadyUnlocked, ref bool noRestriction)
    {
        detail = "";
        if (!init.HasBody) { detail = "\u65b9\u6cd5\u65e0\u51fd\u6570\u4f53"; return 0; }
        if (init.Body.ExceptionHandlers.Count > 0) { detail = "\u542b\u5f02\u5e38\u5904\u7406\u5757"; return 0; }

        IList<Instruction> ins = init.Body.Instructions;
        bool hasLdsfld = ins.Any(i => i.OpCode.Code == Code.Ldsfld && RefName(i) != null &&
                                       RefName(i).Contains(DlLoaderType + "::" + DlLoaderField));

        int dummyIdx = -1, lambdaIdx = -1;
        for (int i = 0; i < ins.Count; i++)
            if (ins[i].OpCode.Code == Code.Ldstr && ins[i].Operand is string && (string)ins[i].Operand == DummyTaskName) dummyIdx = i;

        foreach (MethodDefinition lm in RestrictionLambdas(init.Module))
        {
            for (int i = 0; i < ins.Count; i++)
            {
                string rn = RefName(ins[i]);
                if (rn != null && rn.Contains(lm.FullName)) { lambdaIdx = i; break; }
            }
            if (lambdaIdx >= 0) break;
        }

        if (dummyIdx < 0 && lambdaIdx < 0)
        {
            if (hasLdsfld) { alreadyUnlocked = true; noRestriction = !anyRestrict; detail = noRestriction ? "\u65e0\u9650\u5236\u4ee3\u7801" : "\u5df2\u65ad\u5f00\u9650\u5236\u5206\u652f"; return 0; }
            detail = "\u672a\u627e\u5230\u201c" + DummyTaskName + "\u201d\u4efb\u52a1\u6216\u9650\u5236\u5f02\u5e38\u5f15\u7528";
            return 0;
        }
        if (hasLdsfld && lambdaIdx < 0) { alreadyUnlocked = true; detail = "\u5df2\u5b58\u5728\u89e3\u9501\u5f15\u7528"; return 0; }

        int start = dummyIdx >= 0 ? dummyIdx : BackToTaskName(ins, lambdaIdx);
        if (start < 0) { detail = "\u672a\u80fd\u5b9a\u4f4d\u4efb\u52a1\u53c2\u6570\u8d77\u70b9"; return 0; }
        int end = ForwardToLoaderTaskCtor(ins, start);
        if (end < start) { detail = "\u672a\u80fd\u5b9a\u4f4d LoaderTask \u6784\u9020"; return 0; }
        if (HasOutsideBranchInto(ins, start, end)) { detail = "\u6709\u5916\u90e8\u8df3\u8f6c\u6307\u5411\u5f85\u66ff\u6362\u533a\u57df"; return 0; }

        int offStart = pe.IlOffset(init, ins[start].Offset);
        int total = (pe.IlOffset(init, ins[end].Offset) + InsLen(init, ins, end)) - offStart;
        if (total < 5) { detail = "\u533a\u57df\u8fc7\u77ed"; return 0; }

        var bytes = new byte[total];
        bytes[0] = 0x7E;
        int tok = dlField.MetadataToken.ToInt32();
        bytes[1] = (byte)(tok & 0xFF); bytes[2] = (byte)((tok >> 8) & 0xFF);
        bytes[3] = (byte)((tok >> 16) & 0xFF); bytes[4] = (byte)((tok >> 24) & 0xFF);
        edits.Add(new Edit(offStart, bytes, "LoaderInit \u9650\u5236\u5206\u652f \u2192 ldsfld " + DlLoaderField + " + nop\u00d7" + (total - 5)));
        detail = "\u7b2c " + start + ".." + end + " \u6761\u6307\u4ee4\uff08" + total + " \u5b57\u8282\uff09\u2192 ldsfld " + DlLoaderField;
        return 1;
    }

    static int PlanNopCalls(PeMap pe, ModuleDefinition mod, string typeName, string methodName, List<Edit> edits)
    {
        int n = 0;
        foreach (MethodDefinition m in AllMethods(mod.Types))
        {
            if (!m.HasBody) continue;
            var ins = m.Body.Instructions;
            for (int i = 0; i < ins.Count; i++)
            {
                if (ins[i].OpCode.Code != Code.Call) continue;
                string rn = RefName(ins[i]);
                if (rn == null || !rn.Contains(typeName + "::" + methodName)) continue;
                var mr = ins[i].Operand as MethodReference;
                if (mr == null || mr.Parameters.Count != 0) continue;
                if (InsLen(m, (IList<Instruction>)ins, i) != 5) continue;
                edits.Add(new Edit(pe.IlOffset(m, ins[i].Offset), new byte[5], "\u66f4\u65b0\u68c0\u67e5\u5165\u53e3 \u2192 nop"));
                n++;
            }
        }
        return n;
    }

    static int PlanDropCalls(PeMap pe, ModuleDefinition mod, string typeName, string methodName, List<Edit> edits)
    {
        int n = 0;
        foreach (MethodDefinition m in AllMethods(mod.Types))
        {
            if (!m.HasBody) continue;
            var ins = m.Body.Instructions;
            for (int i = 0; i < ins.Count; i++)
            {
                if (ins[i].OpCode.Code != Code.Call) continue;
                string rn = RefName(ins[i]);
                if (rn == null || !rn.Contains(typeName + "::" + methodName)) continue;
                var mr = ins[i].Operand as MethodReference;
                if (mr == null) continue;
                int pc = mr.Parameters.Count;
                if (pc < 1 || pc > 5) continue;
                if (InsLen(m, (IList<Instruction>)ins, i) != 5) continue;
                var bytes = new byte[5];
                for (int k = 0; k < pc; k++) bytes[k] = 0x26;
                edits.Add(new Edit(pe.IlOffset(m, ins[i].Offset), bytes, "\u81ea\u6211\u66ff\u6362\u8c03\u7528 \u2192 \u6254\u5f03 " + pc + " \u4e2a\u53c2\u6570"));
                n++;
            }
        }
        return n;
    }


    // PCL \u5728\u62d6\u62fd\u5b89\u88c5\u6574\u5408\u5305\u5931\u8d25\u65f6\u4f1a\u541e\u6389\u771f\u5b9e\u5f02\u5e38\uff0c\u53ea\u63d0\u793a\u201c\u65e0\u6cd5\u786e\u5b9a\u9002\u5408\u7684\u64cd\u4f5c\u201d
    // \u8fd9\u91cc\u628a\u90a3\u6b21\u6bd4\u8f83\u5f3a\u5236\u5224\u4e3a\u201c\u76f8\u7b49\u201d\uff0c\u8ba9 PCL \u76f4\u63a5 Hint \u51fa exception.Message
    static int PlanShowRealMessage(PeMap pe, ModuleDefinition mod, List<Edit> edits)
    {
        int n = 0;
        foreach (MethodDefinition m in AllMethods(mod.Types))
        {
            if (!m.HasBody) continue;
            var ins = m.Body.Instructions;
            for (int i = 0; i < ins.Count; i++)
            {
                if (ins[i].OpCode.Code != Code.Ldstr) continue;
                string s = ins[i].Operand as string;
                if (s == null || !RestrictText.IsMatch(s)) continue;
                for (int k = i + 1; k < ins.Count && k <= i + 3; k++)
                {
                    var mr = ins[k].Operand as MethodReference;
                    if (ins[k].OpCode.Code != Code.Call || mr == null || mr.Name != "CompareString") continue;
                    if (mr.DeclaringType == null || mr.DeclaringType.FullName != "Microsoft.VisualBasic.CompilerServices.Operators") continue;
                    edits.Add(new Edit(pe.IlOffset(m, ins[k].Offset), new byte[] { 0x26, 0x26, 0x16, 0x00, 0x00 },
                        "\u6574\u5408\u5305\u5b89\u88c5\u5931\u8d25\u65f6\u663e\u793a\u771f\u5b9e\u9519\u8bef"));
                    n++;
                    break;
                }
            }
        }
        return n;
    }
    static int PlanVisibility(PeMap pe, ModuleDefinition mod, List<Edit> edits)
    {
        int n = 0;
        foreach (MethodDefinition m in AllMethods(mod.Types))
        {
            if (!m.HasBody) continue;
            var ins = m.Body.Instructions;
            for (int i = 1; i < ins.Count - 1; i++)
            {
                bool alreadyVisible = ins[i].OpCode.Code == Code.Ldc_I4_0;
                if (!alreadyVisible && ins[i].OpCode.Code != Code.Ldc_I4_2) continue;
                if (ins[i + 1].OpCode.Code != Code.Callvirt) continue;
                string setter = RefName(ins[i + 1]);
                if (setter == null || !setter.EndsWith("::set_Visibility")) continue;
                if (ins[i - 1].OpCode.Code != Code.Callvirt) continue;
                string getter = RefName(ins[i - 1]);
                if (getter == null || !getter.EndsWith("::get_CardMinecraft")) continue;
                if (InsLen(m, (IList<Instruction>)ins, i) != 1) continue;
                if (alreadyVisible) { n++; continue; }
                edits.Add(new Edit(pe.IlOffset(m, ins[i].Offset), new byte[] { 0x16 }, "\u4fee\u6539\u9875\u7248\u672c\u5361\u7247 \u2192 \u53ef\u89c1"));
                n++;
            }
        }
        return n;
    }

    static int InsLen(MethodDefinition m, IList<Instruction> ins, int idx)
    {
        int start = ins[idx].Offset;
        int next = (idx + 1 < ins.Count) ? ins[idx + 1].Offset : m.Body.CodeSize;
        return next - start;
    }

    static int BackToTaskName(IList<Instruction> ins, int lambdaIdx)
    {
        for (int i = lambdaIdx; i >= 0 && lambdaIdx - i < 24; i--)
            if (ins[i].OpCode.Code == Code.Ldstr && ins[i].Operand is string) return i;
        return -1;
    }

    static int ForwardToLoaderTaskCtor(IList<Instruction> ins, int start)
    {
        for (int i = start; i < ins.Count && i - start < 40; i++)
        {
            var mr = ins[i].Operand as MethodReference;
            if (ins[i].OpCode.Code == Code.Newobj && mr != null && mr.DeclaringType != null &&
                mr.DeclaringType.Name.StartsWith("LoaderTask")) return i;
        }
        return -1;
    }

    static bool HasOutsideBranchInto(IList<Instruction> ins, int start, int end)
    {
        var inside = new HashSet<Instruction>();
        for (int i = start + 1; i <= end; i++) inside.Add(ins[i]);
        for (int i = 0; i < ins.Count; i++)
        {
            if (i >= start && i <= end) continue;
            var t = ins[i].Operand as Instruction;
            if (t != null && inside.Contains(t)) return true;
            var ts = ins[i].Operand as Instruction[];
            if (ts != null) foreach (Instruction x in ts) if (inside.Contains(x)) return true;
        }
        return false;
    }

    static string Verify(ModuleDefinition mod, bool expectUnlock, bool expectUpdateBlocked, bool expectPack)
    {
        var problems = new List<string>();
        if (expectUnlock)
        {
            MethodDefinition init = FindMethod(mod.GetType(InstallPage), InstallInit);
            if (init == null || !init.HasBody) problems.Add("LoaderInit \u4e22\u5931");
            else
            {
                var ins = init.Body.Instructions;
                bool hasLdsfld = ins.Any(i => i.OpCode.Code == Code.Ldsfld && RefName(i) != null && RefName(i).Contains(DlLoaderType + "::" + DlLoaderField));
                bool hasDummy = ins.Any(i => i.OpCode.Code == Code.Ldstr && i.Operand is string && (string)i.Operand == DummyTaskName);
                if (!hasLdsfld) problems.Add("\u7f3a\u5c11 ldsfld " + DlLoaderField);
                if (hasDummy) problems.Add("\u6b8b\u7559\u5047\u4efb\u52a1");
            }
        }
        TypeDefinition secret = mod.GetType(SecretType);
        if (expectUpdateBlocked && secret != null)
        {
            MethodDefinition perm = FindMethod(secret, PermGetter);
            if (perm != null && perm.HasBody)
            {
                var ins = perm.Body.Instructions;
                if (ins.Count < 2 || ins[0].OpCode.Code != Code.Ldc_I4_0 || ins[1].OpCode.Code != Code.Ret)
                    problems.Add("\u6743\u9650\u5f00\u5173\u672a\u751f\u6548");
            }
            if (CountCalls(mod, SecretType, CheckMethod) > 0) problems.Add("\u4ecd\u6709\u68c0\u67e5\u66f4\u65b0\u8c03\u7528");
            if (CountCalls(mod, SecretType, ReplaceMethod) > 0) problems.Add("\u4ecd\u6709\u81ea\u6211\u66ff\u6362\u8c03\u7528");
        }
        if (expectPack)
        {
            TypeDefinition mm = mod.GetType("PCL.ModModpack");
            MethodDefinition mi = null;
            if (mm != null) foreach (MethodDefinition m in mm.Methods) if (m.Name == "ModpackInstall" && m.Parameters.Count == 3) { mi = m; break; }
            if (mi == null || !mi.HasBody) problems.Add("\u6574\u5408\u5305\u5b89\u88c5\u5165\u53e3\u4e22\u5931");
            else
            {
                bool hasDispatch = mi.Body.Instructions.Any(i => i.OpCode.Code == Code.Call && RefName(i) != null && RefName(i).Contains("InstallPack"));
                if (!hasDispatch) problems.Add("\u6574\u5408\u5305\u5b89\u88c5\u6d3e\u53d1\u672a\u751f\u6548");
                if (mi.Body.ExceptionHandlers.Count == 0) problems.Add("\u6574\u5408\u5305\u5b89\u88c5\u5f02\u5e38\u8868\u4e22\u5931");
            }
        }
        return problems.Count == 0 ? "OK" : "FAIL " + string.Join("\u3001", problems.ToArray());
    }

    static IEnumerable<MethodDefinition> RestrictionLambdas(ModuleDefinition mod)
    {
        return AllMethods(mod.Types).Where(m =>
        {
            if (!m.HasBody) return false;
            foreach (Instruction i in m.Body.Instructions)
                if (i.OpCode.Code == Code.Ldstr && i.Operand is string && RestrictText.IsMatch((string)i.Operand)) return true;
            return false;
        });
    }

    static IEnumerable<MethodDefinition> AllMethods(IEnumerable<TypeDefinition> types)
    {
        foreach (TypeDefinition t in types)
        {
            foreach (MethodDefinition m in t.Methods) yield return m;
            foreach (MethodDefinition m in AllMethods(t.NestedTypes)) yield return m;
        }
    }

    static int CountCalls(ModuleDefinition mod, string typeName, string methodName)
    {
        int n = 0;
        foreach (MethodDefinition m in AllMethods(mod.Types))
        {
            if (!m.HasBody) continue;
            foreach (Instruction ins in m.Body.Instructions)
            {
                if (ins.OpCode.Code != Code.Call) continue;
                string rn = RefName(ins);
                if (rn != null && rn.Contains(typeName + "::" + methodName)) n++;
            }
        }
        return n;
    }

    static string RefName(Instruction i)
    {
        var mr = i.Operand as MethodReference;
        if (mr != null) return mr.DeclaringType.FullName + "::" + mr.Name;
        var fr = i.Operand as FieldReference;
        if (fr != null) return fr.DeclaringType.FullName + "::" + fr.Name;
        return null;
    }

    static MethodDefinition FindMethod(TypeDefinition t, string name)
    {
        if (t == null) return null;
        foreach (MethodDefinition m in t.Methods) if (m.Name == name) return m;
        return null;
    }

    static FieldDefinition FindField(TypeDefinition t, string name)
    {
        if (t == null) return null;
        foreach (FieldDefinition f in t.Fields) if (f.Name == name) return f;
        return null;
    }

    static string ConstValue(ModuleDefinition mod, string type, string field)
    {
        FieldDefinition f = FindField(mod.GetType(type), field);
        if (f == null || !f.HasConstant || f.Constant == null) return null;
        return f.Constant.ToString();
    }

    static readonly string[] BrokenOutputs = new string[] {
        "D21A203BE4E37F8F75778A446F181A816A961C4F35334B25DA8B35EE621F6524",
        "D1DD4C11BBF0D2CD999D6B0C5D4AA28CC2021752F475E342F77A7D3FE350B671"
    };
    static bool IsKnownBroken(string sha) { return BrokenOutputs.Contains(sha); }
    static string Sha256(byte[] data)
    {
        using (var sha = SHA256.Create())
        {
            var sb = new StringBuilder();
            foreach (byte b in sha.ComputeHash(data)) sb.Append(b.ToString("X2"));
            return sb.ToString();
        }
    }
}

internal sealed class PeMap
{
    readonly byte[] _d;
    readonly List<uint[]> _secs = new List<uint[]>();
    public PeMap(byte[] data)
    {
        _d = data;
        int pe = BitConverter.ToInt32(data, 0x3C);
        int numSec = BitConverter.ToUInt16(data, pe + 6);
        int optSize = BitConverter.ToUInt16(data, pe + 20);
        int tab = pe + 24 + optSize;
        for (int i = 0; i < numSec; i++)
        {
            int o = tab + i * 40;
            _secs.Add(new uint[] {
                BitConverter.ToUInt32(data, o + 8),
                BitConverter.ToUInt32(data, o + 12),
                BitConverter.ToUInt32(data, o + 16),
                BitConverter.ToUInt32(data, o + 20)
            });
        }
    }
    public int RvaToOffset(uint rva)
    {
        foreach (uint[] s in _secs)
        {
            uint span = Math.Max(s[0], s[2]);
            if (rva >= s[1] && rva < s[1] + span) return (int)(s[3] + (rva - s[1]));
        }
        throw new InvalidOperationException("RVA 0x" + rva.ToString("X") + " not mapped");
    }
    public int IlOffset(MethodDefinition m, int ilOffset)
    {
        int body = RvaToOffset((uint)m.RVA);
        int header = ((_d[body] & 3) == 2) ? 1 : 12;
        return body + header + ilOffset;
    }
}
// ================= 整合包安装恢复 =================
// PCL 的 ModpackInstall 在 RestrictMcDownload 为 True 时被编译器删除了派发代码（Throw 之后全都是死代码）。
// 这里把派发代码重新生成，放到新增的 PE 段 .pclx 中，并把方法 RVA 指过去。

internal sealed class PackJob
{
    public byte[] Payload;
    public int PayloadLen;
    public int NewVa;
    public int RawPtr;
    public int RawSize;
    public int SecHdrOff;
    public int MethodRvaOff;
    public int NewMethodRva;
    public string Detail;

    static void W32(byte[] b, int o, int v) { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); b[o + 2] = (byte)(v >> 16); b[o + 3] = (byte)(v >> 24); }

    public byte[] Apply(byte[] data)
    {
        int total = RawPtr + RawSize;
        if (data.Length > total) total = data.Length;
        var outBuf = new byte[total];
        Array.Copy(data, 0, outBuf, 0, data.Length);
        Array.Copy(Payload, 0, outBuf, RawPtr, PayloadLen);
        for (int i = 0; i < 8; i++) outBuf[SecHdrOff + i] = 0;
        byte[] nm = Encoding.ASCII.GetBytes(".pclx3");
        for (int i = 0; i < nm.Length; i++) outBuf[SecHdrOff + i] = nm[i];
        W32(outBuf, SecHdrOff + 8, PayloadLen);
        W32(outBuf, SecHdrOff + 16, RawSize);
        W32(outBuf, SecHdrOff + 36, unchecked((int)0x60000020));
        W32(outBuf, MethodRvaOff, NewMethodRva);
        return outBuf;
    }
}

internal static class PackRestorer
{
    const string ModpackType = "PCL.ModModpack";
    const string EntryName = "ModpackInstall";

    static readonly string[] InstallerNames = new string[]
    {
        "InstallPackCurseForge", "InstallPackHMCL", "InstallPackMMC", "InstallPackMCBBS",
        "InstallPackModrinth", "InstallPackLauncherPack", "InstallPackCompress"
    };
    // 派发顺序：Case Else 先布置（落入 switch 失败的默认路径），再 0/1/2/3/4/9
    static readonly int[] Order = new int[] { 6, 0, 1, 2, 3, 4, 5 };
    // 每个安装函数的参数来源：a=ldarg（File/InstanceName/Logo），l=ldloc（Archive/BaseFolder）
    static readonly string[] Specs = new string[]
    {
        "a0l1l2a1a2", "a0l1l2", "a0l1l2", "a0l1l2a1", "a0l1l2a1a2", "a0l1l2", "a0l1"
    };

    static int U16(byte[] b, int o) { return b[o] | (b[o + 1] << 8); }
    static int U32(byte[] b, int o) { return b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24); }
    static void W32(List<byte> l, int v) { l.Add((byte)v); l.Add((byte)(v >> 8)); l.Add((byte)(v >> 16)); l.Add((byte)(v >> 24)); }
    static void W16(List<byte> l, int v) { l.Add((byte)v); l.Add((byte)(v >> 8)); }
    static int Align(int v, int a) { return (v + a - 1) / a * a; }
    static int CaseLen(string spec) { return spec.Length / 2 + 5 + 1 + 5; }
    static string ReadName(byte[] b, int o)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 8 && b[o + i] != 0; i++) sb.Append((char)b[o + i]);
        return sb.ToString();
    }

    public static string LastSectionName(byte[] data)
    {
        try
        {
            int peOff = U32(data, 0x3C);
            int numSec = U16(data, peOff + 6);
            int optSize = U16(data, peOff + 20);
            int secTab = peOff + 24 + optSize;
            return ReadName(data, secTab + (numSec - 1) * 40);
        }
        catch { return null; }
    }
    public static PackJob Plan(byte[] data, ModuleDefinition mod, Regex restrict, out string why)
    {
        why = null;
        TypeDefinition t = mod.GetType(ModpackType);
        if (t == null) { why = "\u672a\u627e\u5230\u6574\u5408\u5305\u6a21\u5757 PCL.ModModpack"; return null; }

        MethodDefinition target = null;
        foreach (MethodDefinition m in t.Methods)
            if (m.Name == EntryName && m.Parameters.Count == 3) { target = m; break; }
        if (target == null) { why = "\u672a\u627e\u5230\u4e09\u53c2\u6570\u7684\u6574\u5408\u5305\u5b89\u88c5\u5165\u53e3"; return null; }

        var inst = new MethodDefinition[InstallerNames.Length];
        for (int i = 0; i < InstallerNames.Length; i++)
        {
            foreach (MethodDefinition m in t.Methods) if (m.Name == InstallerNames[i]) { inst[i] = m; break; }
            if (inst[i] == null) { why = "\u6b64\u7248\u672c\u7f3a\u5c11\u5b89\u88c5\u51fd\u6570 " + InstallerNames[i]; return null; }
        }

        // ---- PE ----
        int peOff = U32(data, 0x3C);
        int numSec = U16(data, peOff + 6);
        int optSize = U16(data, peOff + 20);
        int opt = peOff + 24;
        int magic = U16(data, opt);
        int secAlign = U32(data, opt + 32);
        int fileAlign = U32(data, opt + 36);
        int sizeOfImageOff = opt + 56;
        int sizeOfImage = U32(data, sizeOfImageOff);
        int ddBase = (magic == 0x10B) ? opt + 96 : opt + 112;
        int secTab = opt + optSize;
        int lastHdr = secTab + (numSec - 1) * 40;

        var sVa = new int[numSec]; var sVSize = new int[numSec]; var sRaw = new int[numSec]; var sRawSize = new int[numSec]; var sHdr = new int[numSec]; var sName = new string[numSec];
        for (int i = 0; i < numSec; i++)
        {
            int h = secTab + i * 40;
            sName[i] = ReadName(data, h);
            sVSize[i] = U32(data, h + 8);
            sVa[i] = U32(data, h + 12);
            sRawSize[i] = U32(data, h + 16);
            sRaw[i] = U32(data, h + 20);
            sHdr[i] = h;
        }

        Func<int, int> rvaToOff = rva =>
        {
            for (int i = 0; i < numSec; i++)
            {
                int span = Math.Max(sVSize[i], sRawSize[i]);
                if (rva >= sVa[i] && rva < sVa[i] + span) return sRaw[i] + (rva - sVa[i]);
            }
            return -1;
        };

        string lastName = sName[numSec - 1];
        if (lastName == ".pclx3")
        {
            why = "\u8be5\u6587\u4ef6\u5df2\u7531\u672c\u7248\u6062\u590d\u8fc7\u6574\u5408\u5305\u5b89\u88c5\uff0c\u65e0\u9700\u91cd\u590d";
            return null;
        }
        if (lastName != ".reloc" && !lastName.StartsWith(".pclx"))
        {
            why = "PE \u7ed3\u6784\u4e0d\u53d7\u652f\u6301\uff08\u6700\u540e\u4e00\u6bb5\u4e0d\u662f .reloc/.pclx\uff09";
            return null;
        }

        int relocDirOff = ddBase + 5 * 8;
        int relocRva = U32(data, relocDirOff);
        int relocSize = U32(data, relocDirOff + 4);
        int relocFile = relocRva == 0 ? -1 : rvaToOff(relocRva);
        if (relocFile < 0 || relocSize <= 0 || relocSize > 256) { why = "\u91cd\u5b9a\u4f4d\u76ee\u5f55\u5f02\u5e38\uff0c\u8df3\u8fc7\u6574\u5408\u5305\u6062\u590d"; return null; }

        // ---- 方法体 ----
        if (!target.HasBody) { why = "\u6574\u5408\u5305\u5b89\u88c5\u5165\u53e3\u6ca1\u6709\u65b9\u6cd5\u4f53"; return null; }
        MethodBody body = target.Body;
        int codeSize = body.CodeSize;
        int split = -1;
        foreach (Instruction ins in body.Instructions)
        {
            if (ins.OpCode.Code == Code.Ldstr && ins.Operand is string && restrict.IsMatch((string)ins.Operand))
            { split = ins.Offset; break; }
        }
        if (split <= 0)
        {
            bool nativeOk = false;
            foreach (Instruction ins2 in body.Instructions)
            {
                var mr2 = ins2.Operand as MethodReference;
                if (ins2.OpCode.Code == Code.Call && mr2 != null && mr2.Name != null && mr2.Name.StartsWith("InstallPack")) { nativeOk = true; break; }
            }
            why = nativeOk ? "\u8be5\u7248\u672c\u672c\u6765\u5c31\u652f\u6301\u6574\u5408\u5305\u5b89\u88c5\uff0c\u65e0\u9700\u6062\u590d" : "\u672a\u627e\u5230\u9650\u5236\u63d0\u793a\u7684\u629b\u51fa\u4f4d\u7f6e";
            return null;
        }

        // 类型变量（PackType）必须是第 4 个整型局部变量
        if (body.Variables.Count <= 4 || body.Variables[4].VariableType.FullName != "System.Int32")
        { why = "\u65e0\u6cd5\u786e\u5b9a\u6574\u5408\u5305\u7c7b\u578b\u53d8\u91cf\uff08\u7248\u672c\u5dee\u5f02\uff09\uff0c\u8df3\u8fc7"; return null; }
        int assignCount = 0;
        var insList = new List<Instruction>(body.Instructions);
        for (int i = 1; i < insList.Count; i++)
        {
            var vd = insList[i].Operand as VariableDefinition;
            if (vd == null || vd.Index != 4) continue;
            if (insList[i].OpCode.Code != Code.Stloc_S && insList[i].OpCode.Code != Code.Stloc) continue;
            Code pc = insList[i - 1].OpCode.Code;
            if (pc >= Code.Ldc_I4_M1 && pc <= Code.Ldc_I4_8) assignCount++;
        }
        if (assignCount < 3) { why = "\u65e0\u6cd5\u786e\u5b9a\u6574\u5408\u5305\u7c7b\u578b\u53d8\u91cf\uff08\u7248\u672c\u5dee\u5f02\uff09\uff0c\u8df3\u8fc7"; return null; }

        // 外层 finally（Archive 释放）
        ExceptionHandler outer = null;
        foreach (ExceptionHandler eh in body.ExceptionHandlers)
            if (eh.HandlerType == ExceptionHandlerType.Finally && eh.HandlerStart.Offset >= split) { outer = eh; break; }
        if (outer == null) { why = "\u5f02\u5e38\u5904\u7406\u7ed3\u6784\u4e0d\u53d7\u652f\u6301\uff0c\u8df3\u8fc7\u6574\u5408\u5305\u6062\u590d"; return null; }
        int outerStart = outer.HandlerStart.Offset;
        int finLen = codeSize - outerStart;
        if (finLen <= 0 || finLen > 64) { why = "\u5f02\u5e38\u5904\u7406\u7ed3\u6784\u4e0d\u53d7\u652f\u6301\uff0c\u8df3\u8fc7\u6574\u5408\u5305\u6062\u590d"; return null; }

        // ---- 生成代码 ----
        int codeFile = rvaToOff(target.RVA);
        if (codeFile < 0) { why = "\u5b9a\u4f4d\u65b9\u6cd5\u4f53\u5931\u8d25"; return null; }
        int headerLen = ((data[codeFile] & 3) == 2) ? 1 : 12;
        int ilStart = codeFile + headerLen;

        var code = new List<byte>();
        for (int i = 0; i < split; i++) code.Add(data[ilStart + i]);
        code.Add(0x11); code.Add(0x04); // ldloc.s V4
        int swOff = split + 2;
        int switchCount = 10;
        int afterSwitch = swOff + 5 + switchCount * 4;
        var bodyIlOff = new int[7];
        int p = afterSwitch;
        for (int k = 0; k < 7; k++) { bodyIlOff[Order[k]] = p; p += CaseLen(Specs[Order[k]]); }
        int finallyOff = p; p += finLen;
        int endOff = p; p += 2;
        int newCodeSize = p;
        if (code.Count != swOff) { why = "\u91cd\u5efa\u5931\u8d25\uff08\u524d\u7f00\u957f\u5ea6\u4e0d\u7b26\uff09"; return null; }
        code.Add(0x45); W32(code, switchCount);
        if (code.Count != afterSwitch - switchCount * 4) { why = "\u91cd\u5efa\u5931\u8d25\uff08switch \u5934\uff09"; return null; }
        for (int v = 0; v < switchCount; v++)
        {
            int instIndex = v <= 4 ? v : (v == 9 ? 5 : 6);
            W32(code, bodyIlOff[instIndex] - afterSwitch);
        }
        for (int k = 0; k < 7; k++)
        {
            int id = Order[k];
            string spec = Specs[id];
            for (int q = 0; q < spec.Length; q += 2)
            {
                int arg = spec[q + 1] - '0';
                code.Add(spec[q] == 'a' ? (byte)(0x02 + arg) : (byte)(0x06 + arg));
            }
            code.Add(0x28); W32(code, inst[id].MetadataToken.ToInt32());
            code.Add(0x0A);
            code.Add(0xDD); W32(code, endOff - (code.Count + 4));
            if (code.Count != bodyIlOff[id] + CaseLen(spec)) { why = "\u91cd\u5efa\u5931\u8d25\uff08\u5206\u652f\u957f\u5ea6\uff09"; return null; }
        }
        for (int i = 0; i < finLen; i++) code.Add(data[ilStart + outerStart + i]);
        code.Add(0x06); code.Add(0x2A);
        if (code.Count != newCodeSize) { why = "\u91cd\u5efa\u5931\u8d25\uff08\u5c3e\u90e8\u957f\u5ea6\uff09"; return null; }

        // ---- 异常表 ----
        var ehList = new List<int[]>();
        foreach (ExceptionHandler eh in body.ExceptionHandlers)
        {
            if (eh == outer) { ehList.Add(new int[] { 2, eh.TryStart.Offset, finallyOff - eh.TryStart.Offset, finallyOff, finLen, 0 }); continue; }
            int hs = eh.HandlerStart.Offset;
            int he = eh.HandlerEnd == null ? split : eh.HandlerEnd.Offset;
            if (he > split || he <= hs) he = split;
            int ts = eh.TryStart.Offset;
            int te = eh.TryEnd == null ? hs : eh.TryEnd.Offset;
            if (te > hs || te <= ts) te = hs;
            int flags = eh.HandlerType == ExceptionHandlerType.Finally ? 2 : 0;
            int tok = (eh.HandlerType == ExceptionHandlerType.Catch && eh.CatchType != null) ? eh.CatchType.MetadataToken.ToInt32() : 0;
            ehList.Add(new int[] { flags, ts, te - ts, hs, he - hs, tok });
        }

        // ---- 方法块 ----
        var bodyBytes = new List<byte>();
        bodyBytes.Add(0x1B); bodyBytes.Add(0x30);
        W16(bodyBytes, body.MaxStackSize);
        W32(bodyBytes, newCodeSize);
        W32(bodyBytes, body.LocalVarToken.ToInt32());
        foreach (byte x in code) bodyBytes.Add(x);
        while (bodyBytes.Count % 4 != 0) bodyBytes.Add(0);
        int ehSize = 4 + ehList.Count * 24;
        bodyBytes.Add(0x41); bodyBytes.Add((byte)ehSize); bodyBytes.Add(0); bodyBytes.Add(0);
        foreach (int[] e in ehList) { W32(bodyBytes, e[0]); W32(bodyBytes, e[1]); W32(bodyBytes, e[2]); W32(bodyBytes, e[3]); W32(bodyBytes, e[4]); W32(bodyBytes, e[5]); }

        // ---- 新增段 ----
        var payload = new List<byte>();
        for (int i = 0; i < relocSize; i++) payload.Add(data[relocFile + i]);
        while (payload.Count % 8 != 0) payload.Add(0);
        int bodyOff = payload.Count;
        foreach (byte x in bodyBytes) payload.Add(x);

        int lastVa = sVa[numSec - 1];
        int lastRaw = sRaw[numSec - 1];
        int pageRoom = 0x2000 - (lastVa & 0x1FFF);
        if (payload.Count > pageRoom) { why = "\u6700\u540e\u4e00\u6bb5\u5269\u4f59\u7a7a\u95f4\u4e0d\u8db3\uff0c\u8df3\u8fc7\u6574\u5408\u5305\u6062\u590d"; return null; }
        var job = new PackJob();
        job.Payload = payload.ToArray();
        job.PayloadLen = payload.Count;
        job.NewVa = lastVa;
        job.RawPtr = lastRaw;
        job.RawSize = Align(payload.Count, fileAlign);
        job.SecHdrOff = lastHdr;
        job.NewMethodRva = job.NewVa + bodyOff;

        int rvaField = FindMethodRvaField(data, rvaToOff, target.MetadataToken.ToInt32() & 0xFFFFFF, target.RVA, target.Name);
        if (rvaField < 0) { why = "\u5b9a\u4f4d\u65b9\u6cd5\u8868\u5931\u8d25\uff08\u7248\u672c\u5dee\u5f02\uff09\uff0c\u8df3\u8fc7\u6574\u5408\u5305\u6062\u590d"; return null; }
        job.MethodRvaOff = rvaField;
        job.Detail = "\u652f\u6301 CurseForge / HMCL / MMC / MCBBS / Modrinth / \u542f\u52a8\u5668\u6574\u5408\u5305 / \u666e\u901a\u538b\u7f29\u5305";
        return job;
    }

    static int FindMethodRvaField(byte[] data, Func<int, int> rvaToOff, int row, int oldRva, string name)
    {
        int cliRva = 0;
        return FindMethodRvaFieldCore(data, rvaToOff, row, oldRva, name, out cliRva);
    }

    static int FindMethodRvaFieldCore(byte[] data, Func<int, int> rvaToOff, int row, int oldRva, string name, out int cliRvaOut)
    {
        cliRvaOut = 0;
        int peOff = U32(data, 0x3C);
        int opt = peOff + 24;
        int magic = U16(data, opt);
        int ddBase = (magic == 0x10B) ? opt + 96 : opt + 112;
        int cliRva = U32(data, ddBase + 14 * 8);
        cliRvaOut = cliRva;
        int cli = rvaToOff(cliRva);
        if (cli < 0) return -1;
        int md = rvaToOff(U32(data, cli + 8));
        if (md < 0) return -1;
        int verLen = U32(data, md + 12);
        int q = ((md + 16 + verLen) + 3) & ~3;
        int nStreams = U16(data, q + 2); q += 4;
        int tbl = -1, strOff = -1;
        for (int i = 0; i < nStreams; i++)
        {
            int off = U32(data, q);
            int np = q + 8;
            var sb = new StringBuilder();
            while (data[np] != 0) { sb.Append((char)data[np]); np++; }
            np = (np + 1 + 3) & ~3;
            string n = sb.ToString();
            if (n == "#~" || n == "#-") tbl = md + off;
            else if (n == "#Strings") strOff = md + off;
            q = np;
        }
        if (tbl < 0 || strOff < 0) return -1;
        int heapSizes = data[tbl + 6];
        int strIdx = (heapSizes & 1) != 0 ? 4 : 2;
        int guidIdx = (heapSizes & 2) != 0 ? 4 : 2;
        int blobIdx = (heapSizes & 4) != 0 ? 4 : 2;
        uint lo = (uint)U32(data, tbl + 8);
        uint hi = (uint)U32(data, tbl + 12);
        var rows = new int[64];
        int r = tbl + 24;
        for (int i = 0; i < 64; i++)
        {
            bool present = i < 32 ? ((lo & (1u << i)) != 0) : ((hi & (1u << (i - 32))) != 0);
            if (present) { rows[i] = U32(data, r); r += 4; }
        }
        Func<int, int> Idx = tt => rows[tt] < 65536 ? 2 : 4;
        Func<int, int, int, int, int> Coded = (a, b, c, bits) =>
        {
            int mx = Math.Max(a, Math.Max(b, c));
            return mx < (1 << (16 - bits)) ? 2 : 4;
        };
        int o = r;
        if ((lo & 1u) != 0) o += rows[0] * (2 + strIdx + guidIdx * 3);
        if ((lo & 2u) != 0) o += rows[1] * (Coded(rows[0], rows[26], rows[35], 2) + strIdx * 2);
        if ((lo & 4u) != 0) o += rows[2] * (4 + strIdx * 2 + Coded(rows[2], rows[1], rows[27], 2) + Idx(4) + Idx(6));
        if ((lo & 8u) != 0) o += rows[3] * Idx(4);
        if ((lo & 16u) != 0) o += rows[4] * (2 + strIdx + blobIdx);
        if ((lo & 32u) != 0) o += rows[5] * Idx(6);
        int rowSize = 4 + 2 + 2 + strIdx + blobIdx + Idx(8);
        int f = o + (row - 1) * rowSize;
        if (f + rowSize > data.Length) return -1;
        if (U32(data, f) != oldRva) return -1;
        int nameOff = strIdx == 4 ? U32(data, f + 8) : U16(data, f + 8);
        var nm = new StringBuilder();
        int pp = strOff + nameOff;
        while (pp < data.Length && data[pp] != 0) { nm.Append((char)data[pp]); pp++; }
        if (nm.ToString() != name) return -1;
        return f;
    }
}
