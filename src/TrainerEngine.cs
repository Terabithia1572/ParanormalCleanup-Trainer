using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace ParanormalTrainer
{
    public sealed class Feature
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string TurkishName { get; set; }
        public string Category { get; set; }
        public string Type { get; set; }
        public bool Writable { get; set; }
        public long[] Chain { get; set; }
        public bool Frozen { get; set; }
        public string FrozenValue { get; set; }
        public int StringLength { get; set; }
        public long[] OwnerChain { get; set; }
        public string PropertyName { get; set; }
        public string DeclaringClass { get; set; }
        public string PropertyType { get; set; }
        public int PropertyOffset { get; set; }
        public int ComponentOffset { get; set; }
    }

    public sealed class TrainerEngine : IDisposable
    {
        public const string TargetName = "aCleaningHorror-Win64-Shipping";
        public const string SupportedSha256 = "9a06f5834c8a7593e05ec92de6bfeec6cb975bae63a303a9615fbab006fbcc9e";
        private const string Pattern = "4C ?? ?? 4C ?? ?? ?? ?? 48 8B ?? 48 8B ?? 41 FF ?? ?? ?? ?? ?? 48 83";
        private static readonly byte[] ExpectedInstructions = { 0x4c, 0x8b, 0x0f, 0x4c, 0x8d, 0x44, 0x24, 0x40 };
        private RemoteMemory memory;
        private Process process;
        private UeReflection reflection;
        private long hook, cave, player, moduleBase, moduleEnd;
        private byte[] patch;
        private bool testMode = false;
        private readonly Dictionary<string, List<FrozenWrite>> quick = new Dictionary<string, List<FrozenWrite>>();
        private readonly Dictionary<int, FrozenWrite> frozen = new Dictionary<int, FrozenWrite>();
        public List<Feature> Features { get; private set; }
        public bool IsAttached { get { return memory != null; } }
        public bool IsReady { get; private set; }
        public string Status { get; private set; }
        public int ProcessId { get { return process == null ? 0 : process.Id; } }

        private sealed class FrozenWrite
        {
            public Feature Feature;
            public long Address;
            public byte[] Original, Value;
            public int DynamicMaximum;
        }

        public TrainerEngine()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("features.json"))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
                Features = new JavaScriptSerializer().Deserialize<List<Feature>>(reader.ReadToEnd());
            if (Features.Count != 138 || Features.Any(f => f.Chain == null || f.Chain.Length == 0))
                throw new InvalidDataException("Özellik listesi doğrulanamadı.");
            Status = "Oyun bekleniyor. Önce tek oyunculu bir bölüm başlatın.";
        }

        public void Attach()
        {
            if (IsAttached) return;
            var candidates = Process.GetProcessesByName(TargetName);
            if (candidates.Length != 1)
            {
                foreach (var item in candidates) item.Dispose();
                throw new InvalidOperationException(candidates.Length == 0 ? "Oyun bulunamadı. Paranormal Cleanup'ı başlatın." : "Birden fazla oyun işlemi var. Yalnızca birini açık bırakın.");
            }
            Process selected = candidates[0];
            try
            {
                string path = selected.MainModule.FileName;
                byte[] bytes = File.ReadAllBytes(path);
                string hash = Hash(bytes);
                if (!String.Equals(hash, SupportedSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Bu oyun sürümü desteklenen dosyayla eşleşmiyor. Belleğe yazılmadı. SHA-256: " + hash);
                var matches = FindPattern(bytes, Pattern);
                if (matches.Count != 1 || !bytes.Skip(matches[0]).Take(8).SequenceEqual(ExpectedInstructions))
                    throw new InvalidOperationException("CT imzası veya komut sınırları eşleşmiyor. Belleğe yazılmadı.");
                long rva = FileOffsetToRva(bytes, matches[0]);
                process = selected;
                moduleBase = selected.MainModule.BaseAddress.ToInt64();
                moduleEnd = moduleBase + selected.MainModule.ModuleMemorySize;
                memory = new RemoteMemory(selected.Id);
                reflection = new UeReflection(memory,moduleBase);
                hook = moduleBase + rva;
                InstallHook();
                Status = "Bağlandı. Yerel oyuncunun oluşması bekleniyor.";
                Tick();
            }
            catch
            {
                // If the hook was published, Detach must restore it before releasing ownership.
                if (patch != null && memory != null) Detach();
                else Release();
                selected.Dispose();
                throw;
            }
        }

        private void InstallHook()
        {
            if (!memory.Read(hook, 8).SequenceEqual(ExpectedInstructions))
                throw new InvalidOperationException("Oyun kodu zaten değiştirilmiş. Diğer traineri veya CT'yi kapatın.");
            cave = memory.AllocateNear(hook);
            byte[] body = BuildTrampoline(cave, hook, ExpectedInstructions);
            memory.Write(cave + 16, body);
            memory.Flush(cave + 16, body.Length);
            byte[] newPatch = BuildJump(hook, cave + 16, 8);
            try
            {
                memory.WithPaused(process, hook, 8, delegate
                {
                    if (!memory.Read(hook, 8).SequenceEqual(ExpectedInstructions))
                        throw new InvalidOperationException("Bağlantı sırasında oyun kodu değişti.");
                    // Retain ownership even when a partial write or page-protection restoration fails.
                    patch = newPatch;
                    memory.WriteCode(hook, patch);
                    if (!memory.Read(hook, 8).SequenceEqual(patch)) throw new IOException("Kod yaması doğrulanamadı.");
                });
            }
            catch
            {
                if (patch == null) { memory.Free(cave); cave = 0; }
                throw;
            }
        }

        public void Tick()
        {
            if (!IsAttached) return;
            if (process.HasExited) { Release(); Status = "Oyun kapandı. Tüm anahtarlar sıfırlandı."; return; }
            long candidate;
            try { candidate = memory.Pointer(cave); }
            catch { LosePlayer("Oyuncu adresi henüz hazır değil."); return; }
            if (candidate != player)
            {
                DisableAll();
                player = candidate;
            }
            try
            {
                ValidatePlayer();
                IsReady = true;
                Status = "Bağlı • Tek oyunculu oturum • PID " + process.Id;
                foreach (var entry in frozen.Values.Concat(quick.Values.SelectMany(v => v)).ToList())
                {
                    if (Resolve(entry.Feature) != entry.Address)
                        throw new InvalidOperationException("Eşya veya oyuncu adresi değişti. Etkin özellikler kapatıldı.");
                    if (entry.DynamicMaximum != 0)
                    {
                        float max = ReadFloat(Find(entry.DynamicMaximum));
                        if (!Finite(max) || max <= 0 || max > 100000) throw new IOException("Maksimum değer geçersiz.");
                        entry.Value = BitConverter.GetBytes(max);
                    }
                    memory.Write(entry.Address, entry.Value);
                }
            }
            catch (Exception ex) { LosePlayer(ex.Message); }
        }

        private void ValidatePlayer()
        {
            RemoteMemory.CheckPointer(player);
            if (!testMode)
            {
                long vtable = memory.Pointer(player);
                if (vtable < moduleBase || vtable >= moduleEnd) throw new IOException("Oyuncu nesnesi doğrulanamadı.");
                reflection.RequirePlayer(player);
            }
            float maxHealth = ReadFloat(Find(108767)), health = ReadFloat(Find(108766));
            float maxStamina = ReadFloat(Find(108764)), stamina = ReadFloat(Find(108759));
            if (!Finite(maxHealth) || maxHealth <= 0 || maxHealth > 100000 || !Finite(health) || health < -1000 || health > maxHealth * 10 ||
                !Finite(maxStamina) || maxStamina <= 0 || maxStamina > 100000 || !Finite(stamina) || stamina < -1000 || stamina > maxStamina * 10)
                throw new IOException("CT oyuncu değerleri doğrulanamadı. Bölüme girdikten sonra tekrar deneyin.");
            // This CT has no actor filter. Require its entire local-player/controller path plus sane stats.
            byte mode = memory.Read(Resolve(Find(108890)), 1)[0];
            if (mode != 0) throw new InvalidOperationException("Bu trainer yalnızca tek oyunculu oturumda etkinleşir.");
        }

        private void LosePlayer(string reason)
        {
            try { DisableAll(); } catch (Exception ex) { reason += " Geri alma: " + ex.Message; }
            IsReady = false;
            Status = reason;
        }

        public Feature Find(int id) { return Features.First(f => f.Id == id); }
        private long Resolve(Feature feature)
        {
            if(!testMode)
            {
                long owner=player;RemoteMemory.CheckPointer(owner);
                foreach(long offset in feature.OwnerChain)owner=memory.Pointer(owner+offset);
                reflection.RequireProperty(owner,feature);
            }
            long address = player;
            RemoteMemory.CheckPointer(address);
            for (int i = 0; i < feature.Chain.Length - 1; i++) address = memory.Pointer(checked(address + feature.Chain[i]));
            return checked(address + feature.Chain[feature.Chain.Length - 1]);
        }
        private float ReadFloat(Feature f) { return BitConverter.ToSingle(memory.Read(Resolve(f), 4), 0); }
        public string Read(Feature f)
        {
            if (!IsReady) return "—";
            try
            {
                long address = Resolve(f);
                if (f.Type == "String")
                {
                    // CT specifies UTF-16. Read at most 32 characters without assuming allocation capacity.
                    var chars = new StringBuilder();
                    for (int i = 0; i < 32; i++) { char c = (char)BitConverter.ToUInt16(memory.Read(address + i * 2, 2), 0); if (c == 0) break; chars.Append(c); }
                    return chars.ToString();
                }
                var data = memory.Read(address, f.Type == "Byte" ? 1 : 4);
                if (f.Type == "Float") return BitConverter.ToSingle(data, 0).ToString("G7", CultureInfo.InvariantCulture);
                if (f.Type == "Byte") return data[0].ToString(CultureInfo.InvariantCulture);
                return BitConverter.ToInt32(data, 0).ToString(CultureInfo.InvariantCulture);
            }
            catch { return "—"; }
        }
        private void RequireWrite(Feature f)
        {
            if (!IsAttached || !IsReady) throw new InvalidOperationException("Önce oyuna bağlanın ve tek oyunculu bölüme girin.");
            if (!f.Writable) throw new InvalidOperationException("Bu alan salt okunur.");
            if (memory.Pointer(cave) != player) throw new InvalidOperationException("Oyuncu değişti. Bir sonraki yenilemeyi bekleyin.");
            ValidatePlayer();
        }
        public void Apply(Feature f, string value)
        {
            RequireWrite(f);
            if (f.Frozen || quick.Values.SelectMany(v => v).Any(w => w.Feature.Id == f.Id))
                throw new InvalidOperationException("Bu değeri düzenlemeden önce ilgili sabitlemeyi kapatın.");
            byte[] bytes = Encode(f, value);
            ValidateStatWrite(f, bytes);
            long address = Resolve(f);
            memory.Write(address, bytes);
            if (!memory.Read(address, bytes.Length).SequenceEqual(bytes)) throw new IOException("Yazılan değer doğrulanamadı; oyun değeri yeniden hesaplıyor olabilir.");
        }
        public void Freeze(Feature f, string value, bool enabled)
        {
            if (!enabled) { FrozenWrite old; if (frozen.TryGetValue(f.Id, out old)) { Restore(old); frozen.Remove(f.Id); } f.Frozen = false; return; }
            RequireWrite(f);
            if (frozen.ContainsKey(f.Id)) throw new InvalidOperationException("Değeri değiştirmek için sabitlemeyi kapatın.");
            if (quick.Values.SelectMany(v => v).Any(w => w.Feature.Id == f.Id)) throw new InvalidOperationException("Aynı alanı kullanan hızlı özelliği önce kapatın.");
            var write = MakeWrite(f.Id, value, 0);
            memory.Write(write.Address, write.Value);
            frozen.Add(f.Id, write); f.Frozen = true; f.FrozenValue = value;
        }
        public bool QuickEnabled(string key) { return quick.ContainsKey(key); }
        public void ToggleQuick(string key, bool enabled)
        {
            List<FrozenWrite> old;
            if (!enabled)
            {
                if (quick.TryGetValue(key, out old)) { foreach (var w in old) Restore(w); quick.Remove(key); }
                return;
            }
            if (quick.ContainsKey(key)) return;
            var list = new List<FrozenWrite>();
            switch (key)
            {
                case "health": list.Add(MakeWrite(108766, Read(Find(108767)), 108767)); break;
                case "stamina": list.Add(MakeWrite(108759, Read(Find(108764)), 108764)); break;
                case "sprint": list.Add(MakeWrite(108776, "2", 0)); break;
                case "jump": list.Add(MakeWrite(108857, "900", 0)); break;
                case "revive": list.Add(MakeWrite(108760, "0.25", 0)); break;
                case "light": list.Add(MakeWrite(108774, "3", 0)); break;
                case "fov": list.Add(MakeWrite(108904, "110", 0)); break;
                default: throw new ArgumentException("Bilinmeyen hızlı özellik.");
            }
            try { foreach (var w in list) memory.Write(w.Address, w.Value); quick.Add(key, list); }
            catch { foreach (var w in list) Restore(w); throw; }
        }
        private FrozenWrite MakeWrite(int id, string value, int max)
        {
            Feature feature = Find(id); RequireWrite(feature);
            if (frozen.ContainsKey(id)) throw new InvalidOperationException("Önce bu alanın düzenleyici sabitlemesini kapatın.");
            byte[] bytes = Encode(feature, value); long address = Resolve(feature);
            ValidateStatWrite(feature, bytes);
            return new FrozenWrite { Feature = feature, Address = address, Original = memory.Read(address, bytes.Length), Value = bytes, DynamicMaximum = max };
        }
        private void ValidateStatWrite(Feature f, byte[] bytes)
        {
            if (f.Type != "Float") return;
            float value = BitConverter.ToSingle(bytes, 0);
            int maximum = f.Id == 108766 ? 108767 : f.Id == 108759 ? 108764 : 0;
            int current = f.Id == 108767 ? 108766 : f.Id == 108764 ? 108759 : 0;
            if (maximum != 0 && (value < 0 || value > ReadFloat(Find(maximum))))
                throw new ArgumentException("Mevcut sağlık/dayanıklılık, sıfır ile azami değer arasında olmalı.");
            if (current != 0 && (value <= 0 || value > 100000 || value < ReadFloat(Find(current))))
                throw new ArgumentException("Azami değer mevcut değerden küçük olamaz ve 0–100.000 aralığında olmalı.");
        }
        private void Restore(FrozenWrite entry)
        {
            if (memory == null || process.HasExited) return;
            // Never follow a stale object address into a new level or overwrite changes by the game.
            long address;
            try { if (memory.Pointer(cave) != player) return; address = Resolve(entry.Feature); }
            catch { return; }
            if (address != entry.Address) return;
            byte[] current;
            try { current = memory.Read(address, entry.Value.Length); } catch { return; }
            if (current.SequenceEqual(entry.Value)) memory.Write(address, entry.Original);
        }
        public void DisableAll()
        {
            Exception failure = null;
            foreach (var w in frozen.Values.Concat(quick.Values.SelectMany(v => v)).ToList())
                try { Restore(w); } catch (Exception ex) { failure = ex; }
            frozen.Clear(); quick.Clear(); foreach (var f in Features) f.Frozen = false;
            if (failure != null) throw new IOException("Bazı sabit değerler geri alınamadı: " + failure.Message, failure);
        }
        public void Detach()
        {
            if (!IsAttached) return;
            if (process.HasExited) { Release(); return; }
            string restoreWarning = null;
            try { DisableAll(); } catch (Exception ex) { restoreWarning = ex.Message; }
            if (patch != null)
            {
                memory.WithPaused(process, hook, 8, delegate
                {
                    byte[] current = memory.Read(hook, 8);
                    if (!current.SequenceEqual(patch) && !current.SequenceEqual(ExpectedInstructions))
                        throw new IOException("Oyun kodu başka bir araç tarafından değiştirilmiş; bağlantı güvenle kaldırılamadı. Oyunu kapatın.");
                    memory.WriteCode(hook, ExpectedInstructions);
                    if (!memory.Read(hook, 8).SequenceEqual(ExpectedInstructions)) throw new IOException("Özgün komutlar geri yüklenemedi. Oyunu kapatın.");
                });
                // Keep 4 KiB until process exit: a thread may still be executing inside the trampoline.
                patch = null;
            }
            Release();
            Status = restoreWarning ?? "Bağlantı kesildi. Özgün oyun komutları geri yüklendi.";
        }
        private void Release()
        {
            frozen.Clear(); quick.Clear(); foreach (var f in Features) f.Frozen = false;
            if (memory != null) memory.Dispose(); if (process != null) process.Dispose();
            memory = null; process = null; reflection = null; patch = null; hook = cave = player = 0; IsReady = false;
        }
        public void Dispose() { Detach(); }
        public static bool Finite(float value) { return !Single.IsNaN(value) && !Single.IsInfinity(value); }
        public static byte[] Encode(Feature f, string text)
        {
            string normalized = (text ?? "").Trim().Replace(',', '.');
            if (f.Type == "Float")
            {
                float value;
                if (!Single.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || !Finite(value) || Math.Abs(value) > 10000000)
                    throw new ArgumentException("Geçerli ve sonlu bir sayı girin (en fazla 10.000.000).");
                return BitConverter.GetBytes(value);
            }
            if (f.Type == "Byte") { byte value; if (!Byte.TryParse(normalized, out value) || value > 1) throw new ArgumentException("Bu alan için 0 (kapalı) veya 1 (açık) girin."); return new[] { value }; }
            if (f.Type == "4 Bytes") { int value; if (!Int32.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) throw new ArgumentException("32 bit tam sayı girin."); return BitConverter.GetBytes(value); }
            throw new ArgumentException("Bu veri türü düzenlenemez.");
        }
        public static string Hash(byte[] data) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant(); }
        public static List<int> FindPattern(byte[] data, string pattern)
        {
            int[] bytes = pattern.Split(' ').Select(p => p == "??" ? -1 : Convert.ToInt32(p, 16)).ToArray();
            var result = new List<int>();
            for (int i = 0; i <= data.Length - bytes.Length; i++)
            {
                if (bytes[0] != data[i]) continue;
                int j = 1; while (j < bytes.Length && (bytes[j] < 0 || data[i + j] == bytes[j])) j++;
                if (j == bytes.Length) result.Add(i);
            }
            return result;
        }
        public static long FileOffsetToRva(byte[] pe, int offset)
        {
            int header = BitConverter.ToInt32(pe, 0x3c);
            if (BitConverter.ToUInt32(pe, header) != 0x4550 || BitConverter.ToUInt16(pe, header + 4) != 0x8664) throw new InvalidDataException("x64 PE bekleniyordu.");
            int count = BitConverter.ToUInt16(pe, header + 6), optional = BitConverter.ToUInt16(pe, header + 20), sections = header + 24 + optional;
            for (int i = 0; i < count; i++)
            {
                int s = sections + i * 40, start = BitConverter.ToInt32(pe, s + 20), size = BitConverter.ToInt32(pe, s + 16);
                if (offset >= start && offset < start + size)
                {
                    if ((BitConverter.ToUInt32(pe, s + 36) & 0x20000000) == 0) throw new InvalidDataException("İmza çalıştırılabilir bölümde değil.");
                    return BitConverter.ToUInt32(pe, s + 12) + (long)offset - start;
                }
            }
            throw new InvalidDataException("İmza adresi eşlenemedi.");
        }
        public static byte[] BuildJump(long from, long to, int length)
        {
            long delta = to - from - 5;
            if (length < 5 || delta < Int32.MinValue || delta > Int32.MaxValue) throw new ArgumentOutOfRangeException("to", "Yakın atlama aralık dışında.");
            byte[] bytes = Enumerable.Repeat((byte)0x90, length).ToArray(); bytes[0] = 0xE9; Buffer.BlockCopy(BitConverter.GetBytes((int)delta), 0, bytes, 1, 4); return bytes;
        }
        public static byte[] BuildTrampoline(long allocation, long origin, byte[] original)
        {
            // mov [rip-23],rdi; mov r9,[rdi]; lea r8,[rsp+40h]; jmp original+8
            // The stolen instructions have no RIP-relative operands. No flags/registers are clobbered by capture.
            var bytes = new List<byte>(new byte[] { 0x48, 0x89, 0x3d });
            bytes.AddRange(BitConverter.GetBytes(-23)); bytes.AddRange(original);
            bytes.AddRange(BuildJump(allocation + 16 + bytes.Count, origin + 8, 5)); return bytes.ToArray();
        }
#if TESTING
        internal void AttachFixture(int pid, long slot, long code, long start, long end)
        {
            testMode = true; process = Process.GetProcessById(pid); memory = new RemoteMemory(pid); cave = slot; hook = code; moduleBase = start; moduleEnd = end; Tick();
        }
        internal void AttachHookFixture(int pid, long code)
        {
            testMode = true; process = Process.GetProcessById(pid); memory = new RemoteMemory(pid); hook = code;
            InstallHook(); Tick();
        }
#endif
    }

    internal sealed class RemoteMemory : IDisposable
    {
        private IntPtr handle;
        internal IntPtr Handle { get { return handle; } }
        public RemoteMemory(int pid) : this(pid, false) { }
        public RemoteMemory(int pid, bool readOnly) { handle = OpenProcess(readOnly ? 0x410u : 0x438u, false, pid); if (handle == IntPtr.Zero) throw Error("Oyun belleğine erişilemiyor"); }
        public static void CheckPointer(long p) { if (p < 0x10000 || p > 0x00007FFFFFFFFFFF || (p & 7) != 0) throw new IOException("Geçersiz nesne adresi."); }
        public long Pointer(long address) { long p = BitConverter.ToInt64(Read(address, 8), 0); CheckPointer(p); return p; }
        public byte[] Read(long address, int count)
        {
            byte[] result = new byte[count]; IntPtr read;
            if (!ReadProcessMemory(handle, new IntPtr(address), result, new IntPtr(count), out read) || read.ToInt64() != count) throw Error("Bellek okunamadı");
            return result;
        }
        public void Write(long address, byte[] bytes)
        {
            IntPtr written; if (!WriteProcessMemory(handle, new IntPtr(address), bytes, new IntPtr(bytes.Length), out written) || written.ToInt64() != bytes.Length) throw Error("Bellek yazılamadı");
        }
        public void Flush(long address, int count) { if (!FlushInstructionCache(handle, new IntPtr(address), new IntPtr(count))) throw Error("Komut önbelleği yenilenemedi"); }
        public void WriteCode(long address, byte[] bytes)
        {
            uint previous; if (!VirtualProtectEx(handle, new IntPtr(address), new IntPtr(bytes.Length), 0x40, out previous)) throw Error("Kod koruması değiştirilemedi");
            try { Write(address, bytes); Flush(address, bytes.Length); }
            finally { uint ignored; if (!VirtualProtectEx(handle, new IntPtr(address), new IntPtr(bytes.Length), previous, out ignored)) throw Error("Kod koruması geri yüklenemedi"); }
        }
        public long AllocateNear(long origin)
        {
            long aligned = origin & ~0xffffL;
            for (long distance = 0x10000; distance < 0x70000000; distance += 0x10000)
            {
                foreach (long candidate in new[] { aligned + distance, aligned - distance })
                {
                    if (candidate < 0x10000) continue;
                    IntPtr found = VirtualAllocEx(handle, new IntPtr(candidate), new IntPtr(4096), 0x3000, 0x40);
                    if (found != IntPtr.Zero) return found.ToInt64();
                }
            }
            throw new IOException("Oyun kodunun yakınında bellek ayrılamadı.");
        }
        public void Free(long address) { if (address != 0 && !VirtualFreeEx(handle, new IntPtr(address), IntPtr.Zero, 0x8000)) throw Error("Bellek serbest bırakılamadı"); }
        public void WithPaused(Process target, long patchStart, int patchLength, Action action)
        {
            // Request suspension only for this selected target; all threads must have inspectable RIPs.
            IntPtr suspend = OpenProcess(0x800, false, target.Id);
            if (suspend == IntPtr.Zero) throw Error("Oyun kısa süreli duraklatılamadı");
            try
            {
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    if (NtSuspendProcess(suspend) != 0) throw new IOException("Oyun duraklatılamadı.");
                    bool busy = false;
                    try
                    {
                        target.Refresh();
                        foreach (ProcessThread thread in target.Threads)
                        {
                            IntPtr th = OpenThread(0x0008, false, (uint)thread.Id);
                            if (th == IntPtr.Zero) throw Error("İş parçacığı doğrulanamadı");
                            IntPtr raw = Marshal.AllocHGlobal(1248);
                            try
                            {
                                IntPtr context = new IntPtr((raw.ToInt64() + 15) & ~15L);
                                Marshal.Copy(new byte[1232], 0, context, 1232); Marshal.WriteInt32(context, 48, 0x100001);
                                if (!GetThreadContext(th, context)) throw Error("Komut adresi okunamadı");
                                long rip = Marshal.ReadInt64(context, 248);
                                if (rip >= patchStart && rip < patchStart + patchLength) busy = true;
                            }
                            finally { Marshal.FreeHGlobal(raw); CloseHandle(th); }
                        }
                        if (!busy) { action(); return; }
                    }
                    finally { if (NtResumeProcess(suspend) != 0) throw new IOException("Oyun devam ettirilemedi. Oyunu yeniden başlatın."); }
                    Thread.Sleep(10);
                }
                throw new IOException("Yama bölgesi kullanımda; yeniden bağlanmayı deneyin.");
            }
            finally { CloseHandle(suspend); }
        }
        private static Exception Error(string message) { return new Win32Exception(Marshal.GetLastWin32Error(), message + " (Windows " + Marshal.GetLastWin32Error() + ")"); }
        public void Dispose() { if (handle != IntPtr.Zero) { CloseHandle(handle); handle = IntPtr.Zero; } }
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] data, IntPtr size, out IntPtr read);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] data, IntPtr size, out IntPtr written);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, IntPtr size, uint allocation, uint protect);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool VirtualFreeEx(IntPtr process, IntPtr address, IntPtr size, uint freeType);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool VirtualProtectEx(IntPtr process, IntPtr address, IntPtr size, uint protect, out uint previous);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool FlushInstructionCache(IntPtr process, IntPtr address, IntPtr count);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenThread(uint access, bool inherit, uint id);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetThreadContext(IntPtr thread, IntPtr context);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("ntdll.dll")] private static extern int NtSuspendProcess(IntPtr process);
        [DllImport("ntdll.dll")] private static extern int NtResumeProcess(IntPtr process);
    }
}
