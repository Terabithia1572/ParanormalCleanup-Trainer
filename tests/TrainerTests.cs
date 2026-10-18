using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace ParanormalTrainer
{
    public static class TrainerTests
    {
        private static int passed;
        private static void Check(bool condition, string message) { if (!condition) throw new Exception("FAIL: " + message); passed++; Console.WriteLine("PASS: " + message); }
        private static void Throws(Action action, string name) { bool failed = false; try { action(); } catch { failed = true; } Check(failed, name); }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void VoidFunction();
        [DllImport("kernel32.dll")] private static extern IntPtr VirtualAlloc(IntPtr address, UIntPtr size, uint allocation, uint protect);
        [STAThread]
        public static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--fixture") return Fixture();
            string root = args.Length > 0 ? args[0] : Environment.CurrentDirectory;
            try
            {
                using (var engine = new TrainerEngine())
                {
                    Check(engine.Features.Count == 138, "all 138 CT fields embedded");
                    Check(engine.Features.Select(f => f.Id).Distinct().Count() == 138, "feature IDs unique");
                    Check(engine.Features.All(f => !String.IsNullOrWhiteSpace(f.TurkishName)), "all features have Turkish labels");
                    Check(engine.Features.Select(f => f.Category).Distinct().Count() == 7, "seven editor categories");
                    Check(!engine.Find(108773).Writable && !engine.Find(108814).Writable && !engine.Find(108815).Writable && !engine.Find(108890).Writable, "strings, delegates and session mode are read-only");
                    Check(engine.Find(108896).Chain.SequenceEqual(new long[] {0x258,0x298,0x70,0x80,0x388}), "multi-level CT pointer chain order");
                    Check(BitConverter.ToSingle(TrainerEngine.Encode(engine.Find(108766), "12,5"), 0) == 12.5f, "Turkish decimal comma accepted");
                    Throws(delegate { TrainerEngine.Encode(engine.Find(108766), "NaN"); }, "NaN rejected");
                    Throws(delegate { TrainerEngine.Encode(engine.Find(108766), "Infinity"); }, "infinity rejected");
                    Throws(delegate { TrainerEngine.Encode(engine.Find(108810), "256"); }, "byte overflow rejected");
                    Throws(delegate { TrainerEngine.Encode(engine.Find(108773), "text"); }, "unsafe string writes rejected");
                    Throws(delegate { engine.Apply(engine.Find(108766), "100"); }, "write while detached rejected");
                    Throws(delegate { TrainerEngine.BuildJump(0x10000, 0x100000000L, 8); }, "far jump rejected");
                    byte[] jump = TrainerEngine.BuildJump(0x10000, 0x20000, 8);
                    Check(jump[0] == 0xe9 && BitConverter.ToInt32(jump, 1) + 0x10005 == 0x20000 && jump.Skip(5).All(b=>b==0x90), "rel32 jump encoding and padding");
                    Check(TrainerEngine.FindPattern(new byte[]{0x4c,1,2,0x4c,0x4c,9,2,0x4c}, "4C ?? 02 4C").Count == 2, "signature ambiguity detected");
                }
                RunRemoteTests();
                string game = Environment.GetEnvironmentVariable("PARANORMAL_GAME_EXE");
                if (!String.IsNullOrWhiteSpace(game) && !File.Exists(game)) throw new FileNotFoundException("PARANORMAL_GAME_EXE does not point to an existing game executable.", game);
                if (File.Exists(game))
                {
                    byte[] data = File.ReadAllBytes(game);
                    Check(TrainerEngine.Hash(data) == TrainerEngine.SupportedSha256, "installed game SHA-256 bound to profile");
                    var hits = TrainerEngine.FindPattern(data, "4C ?? ?? 4C ?? ?? ?? ?? 48 8B ?? 48 8B ?? 41 FF ?? ?? ?? ?? ?? 48 83");
                    Check(hits.Count == 1, "exactly one CT signature in installed game");
                    Check(data.Skip(hits[0]).Take(8).SequenceEqual(new byte[]{0x4c,0x8b,0x0f,0x4c,0x8d,0x44,0x24,0x40}), "stolen instructions are complete and relocatable");
                    Check(TrainerEngine.FileOffsetToRva(data, hits[0]) > 0, "signature maps to executable PE section");
                }
                Console.WriteLine("TOTAL: " + passed + " passed. This suite uses a controlled child process; separate live results are in docs/verification/live-test-result.json.");
                Directory.CreateDirectory(Path.Combine(root, "analysis"));
                File.WriteAllText(Path.Combine(root, "analysis", "test-result.txt"), "Passed: " + passed + "\r\nScope: controlled child process; optional static installed-game profile\r\nLive game evidence: see docs/verification/live-test-result.json\r\nUTC: " + DateTime.UtcNow.ToString("o"));
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
        private static void RunRemoteTests()
        {
            var start = new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, "--fixture") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (Process child = Process.Start(start))
            {
                try
                {
                    string[] parts = child.StandardOutput.ReadLine().Split('|');
                    long slot = Int64.Parse(parts[0]), pawn = Int64.Parse(parts[1]), gameInstance = Int64.Parse(parts[2]), entry = Int64.Parse(parts[3]), wrapper = Int64.Parse(parts[4]);
                    using (var remote = new RemoteMemory(child.Id))
                    using (var engine = new TrainerEngine())
                    {
                        engine.AttachFixture(child.Id, slot, entry, entry, entry+4096);
                        Check(engine.IsReady, "synthetic local player validated in a separate process");
                        Check(engine.Read(engine.Find(108766)) == "80", "cross-process float read");
                        Throws(delegate{engine.Apply(engine.Find(108767),"0");}, "invalid maximum rejected before writing");
                        Throws(delegate{engine.Apply(engine.Find(108767),"50");}, "maximum below current health rejected");
                        Throws(delegate{engine.Apply(engine.Find(108766),"200");}, "health above maximum rejected");
                        Check(engine.Read(engine.Find(108767))=="100", "failed edits leave target memory unchanged");
                        engine.Apply(engine.Find(108766), "75");
                        Check(BitConverter.ToSingle(remote.Read(pawn+0x650,4),0)==75, "cross-process value write and verification");
                        engine.Freeze(engine.Find(108766), "95", true);
                        remote.Write(pawn+0x650,BitConverter.GetBytes(60f)); engine.Tick();
                        Check(BitConverter.ToSingle(remote.Read(pawn+0x650,4),0)==95, "freeze reapplies after remote change");
                        engine.Freeze(engine.Find(108766), "", false);
                        Check(BitConverter.ToSingle(remote.Read(pawn+0x650,4),0)==75, "unfreeze restores previous value");
                        engine.ToggleQuick("health",true); engine.Tick();
                        Check(engine.Read(engine.Find(108766)) == "100", "quick health follows maximum");
                        remote.Write(pawn+0x654,BitConverter.GetBytes(150f)); engine.Tick();
                        Check(engine.Read(engine.Find(108766)) == "150", "quick health follows changing maximum");
                        engine.ToggleQuick("health",false);
                        Check(engine.Read(engine.Find(108766)) == "75", "quick health restores prior value");
                        engine.ToggleQuick("stamina",true); engine.Tick(); Check(engine.Read(engine.Find(108759))=="100", "quick stamina writes maximum"); engine.DisableAll();
                        engine.ToggleQuick("sprint",true); Check(engine.Read(engine.Find(108776))=="2", "sprint multiplier preset"); engine.DisableAll();
                        engine.ToggleQuick("jump",true); Check(engine.Read(engine.Find(108857))=="900", "jump preset"); engine.DisableAll();
                        engine.ToggleQuick("revive",true); Check(engine.Read(engine.Find(108760))=="0.25", "revive preset"); engine.DisableAll();
                        engine.ToggleQuick("light",true); Check(engine.Read(engine.Find(108774))=="3", "flashlight preset"); engine.DisableAll();
                        engine.ToggleQuick("fov",true); Check(engine.Read(engine.Find(108904))=="110", "FOV preset"); engine.DisableAll();
                        engine.Freeze(engine.Find(108766), "90", true);
                        Throws(delegate{engine.ToggleQuick("health",true);}, "overlapping quick and editor freeze rejected"); engine.DisableAll();
                        remote.Write(gameInstance+0x2a0,new byte[]{1}); engine.Tick();
                        Check(!engine.IsReady, "multiplayer state fails closed");
                        Throws(delegate{engine.Apply(engine.Find(108766),"100");}, "writes blocked in multiplayer");
                        remote.Write(gameInstance+0x2a0,new byte[]{0}); engine.Tick();
                        Check(engine.IsReady, "local state recovers without enabling cheats");
                        engine.ToggleQuick("health", true); remote.Write(slot, BitConverter.GetBytes(0L)); engine.Tick();
                        Check(!engine.IsReady && !engine.QuickEnabled("health"), "lost player clears toggles");
                        remote.Write(slot,BitConverter.GetBytes(pawn)); engine.Tick();
                        long allocation = remote.AllocateNear(entry);
                        byte[] original = remote.Read(entry,8);
                        byte[] trampoline = TrainerEngine.BuildTrampoline(allocation,entry,original);
                        remote.Write(allocation+16,trampoline); remote.Flush(allocation+16,trampoline.Length);
                        byte[] patch = TrainerEngine.BuildJump(entry,allocation+16,8);
                        remote.WithPaused(child,entry,8,delegate {remote.WriteCode(entry,patch);});
                        child.StandardInput.WriteLine("invoke"); child.StandardInput.Flush();
                        Check(child.StandardOutput.ReadLine()=="invoked", "patched native code executes in controlled child");
                        Check(remote.Pointer(allocation)==pawn, "trampoline captures RDI and returns correctly");
                        remote.WithPaused(child,entry,8,delegate {remote.WriteCode(entry,original);});
                        Check(remote.Read(entry,8).SequenceEqual(original), "original code restored with thread contexts checked");
                        child.StandardInput.WriteLine("invoke"); child.StandardInput.Flush();
                        Check(child.StandardOutput.ReadLine()=="invoked", "restored native code executes correctly");
                        remote.Free(allocation); // fixture call has synchronously returned, no thread executes cave
                        engine.Detach(); Check(!engine.IsAttached, "detach clears process ownership");
                        engine.AttachHookFixture(child.Id, entry);
                        Check(engine.IsAttached && !engine.IsReady, "production hook installs and waits for capture");
                        child.StandardInput.WriteLine("invoke"); child.StandardInput.Flush();
                        Check(child.StandardOutput.ReadLine()=="invoked", "production hook executes without register damage");
                        engine.Tick(); Check(engine.IsReady, "production hook feeds engine player resolution");
                        engine.ToggleQuick("health",true); engine.Detach();
                        Check(remote.Read(entry,8).SequenceEqual(original), "production Detach restores original instructions");
                        Check(!engine.IsAttached && !engine.QuickEnabled("health"), "production Detach resets all feature state");
                        engine.AttachFixture(child.Id,slot,entry,entry,entry+4096);
                        child.StandardInput.WriteLine("exit"); child.StandardInput.Flush(); child.WaitForExit(5000); engine.Tick();
                        Check(!engine.IsAttached && !engine.IsReady, "process exit resets attachment and state");
                    }
                }
                finally { if (!child.HasExited) { child.StandardInput.WriteLine("exit"); child.StandardInput.Flush(); if (!child.WaitForExit(5000)) child.Kill(); } }
            }
        }
        private static int Fixture()
        {
            IntPtr block = VirtualAlloc(IntPtr.Zero,new UIntPtr(0x20000),0x3000,0x40);
            long b=block.ToInt64(), pawn=b+0x1000, controller=b+0x3000, local=b+0x5000, viewport=b+0x7000, game=b+0x9000, perks=b+0xb000, move=b+0xd000, entry=b+0xf000, wrapper=b+0x10000;
            Marshal.WriteInt64(new IntPtr(b),pawn); Marshal.WriteInt64(new IntPtr(pawn),pawn);
            Marshal.WriteInt64(new IntPtr(pawn+0x258),controller); Marshal.WriteInt64(new IntPtr(controller+0x298),local); Marshal.WriteInt64(new IntPtr(local+0x70),viewport); Marshal.WriteInt64(new IntPtr(viewport+0x80),game);
            Marshal.WriteInt64(new IntPtr(pawn+0x530),perks); Marshal.WriteInt64(new IntPtr(pawn+0x288),move);
            Action<long,float> set = delegate(long p,float v){Marshal.Copy(BitConverter.GetBytes(v),0,new IntPtr(p),4);};
            set(pawn+0x650,80); set(pawn+0x654,100); set(pawn+0x5b4,60); set(pawn+0x62c,100); set(pawn+0x5d8,5);set(perks+0xc8,1);set(perks+0xd0,1);set(move+0x158,400);set(game+0x698,90);
            byte[] code={0x4c,0x8b,0x0f,0x4c,0x8d,0x44,0x24,0x40,0xc3}; Marshal.Copy(code,0,new IntPtr(entry),code.Length);
            // Preserve nonvolatile RDI, reserve x64 shadow space, point RDI at pawn, call entry, restore.
            var instructions=new System.Collections.Generic.List<byte>(new byte[]{0x57,0x48,0x83,0xec,0x20,0x48,0xbf});instructions.AddRange(BitConverter.GetBytes(pawn));instructions.AddRange(new byte[]{0x48,0xb8});instructions.AddRange(BitConverter.GetBytes(entry));instructions.AddRange(new byte[]{0xff,0xd0,0x48,0x83,0xc4,0x20,0x5f,0xc3});Marshal.Copy(instructions.ToArray(),0,new IntPtr(wrapper),instructions.Count);
            var invoke=(VoidFunction)Marshal.GetDelegateForFunctionPointer(new IntPtr(wrapper),typeof(VoidFunction));
            Console.WriteLine(b+"|"+pawn+"|"+game+"|"+entry+"|"+wrapper);Console.Out.Flush();
            string command;while((command=Console.ReadLine())!=null && command!="exit") if(command=="invoke"){invoke();Console.WriteLine("invoked");Console.Out.Flush();}
            return 0;
        }
    }
}
