using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;

namespace ParanormalTrainer
{
    // Explicitly read-only inspection of the already-running trainer's known hook.
    internal static class LiveProbe
    {
        public static int Main()
        {
            try
            {
                var game = Process.GetProcessesByName(TrainerEngine.TargetName).Single();
                byte[] file = File.ReadAllBytes(game.MainModule.FileName);
                if (TrainerEngine.Hash(file) != TrainerEngine.SupportedSha256) throw new Exception("Unsupported game image");
                var hits = TrainerEngine.FindPattern(file, "4C ?? ?? 4C ?? ?? ?? ?? 48 8B ?? 48 8B ?? 41 FF ?? ?? ?? ?? ?? 48 83");
                if (hits.Count != 1) throw new Exception("Ambiguous signature");
                long origin = game.MainModule.BaseAddress.ToInt64() + TrainerEngine.FileOffsetToRva(file, hits[0]);
                using (var memory = new RemoteMemory(game.Id, true))
                {
                    byte[] code = memory.Read(origin, 8);
                    if (code[0] != 0xe9) { Console.WriteLine("Hook not installed. Original bytes: " + BitConverter.ToString(code)); return 2; }
                    long cave = origin + 5 + BitConverter.ToInt32(code, 1) - 16;
                    byte[] body = memory.Read(cave + 16, 20);
                    byte[] expected = TrainerEngine.BuildTrampoline(cave, origin, file.Skip(hits[0]).Take(8).ToArray());
                    if (!body.SequenceEqual(expected)) throw new Exception("Not this trainer's trampoline");
                    long player = BitConverter.ToInt64(memory.Read(cave, 8),0);
                    var output = new System.Collections.Generic.Dictionary<string, object>();
                    output["pid"] = game.Id; output["hook"] = origin.ToString("X"); output["cave"] = cave.ToString("X"); output["player"] = player.ToString("X"); output["readOnly"] = true;
                    var engine = new TrainerEngine();
                    var reflection = new UeReflection(memory,game.MainModule.BaseAddress.ToInt64());
                    reflection.RequirePlayer(player);
                    var invalid = new System.Collections.Generic.List<string>();
                    int verified=0;
                    foreach(var f in engine.Features)
                    {
                        try {long owner=player;foreach(long offset in f.OwnerChain)owner=memory.Pointer(owner+offset);reflection.RequireProperty(owner,f);verified++;}
                        catch(Exception ex){invalid.Add(f.Id+": "+ex.Message);}
                    }
                    output["propertiesVerified"]=verified;output["unavailableProperties"]=invalid;
                    foreach (int id in new[] {108766,108767,108759,108764,108776,108857,108760,108774,108904,108890,108896})
                    {
                        var f = engine.Find(id);
                        try
                        {
                            long address=player; RemoteMemory.CheckPointer(address);
                            for (int i=0;i<f.Chain.Length-1;i++) address=memory.Pointer(address+f.Chain[i]);
                            address+=f.Chain[f.Chain.Length-1];
                            byte[] bytes=memory.Read(address,f.Type=="Byte"?1:4);
                            output[f.Name+"_"+id]=(object)(f.Type=="Float"?BitConverter.ToSingle(bytes,0).ToString("G7",System.Globalization.CultureInfo.InvariantCulture):f.Type=="Byte"?bytes[0].ToString():BitConverter.ToInt32(bytes,0).ToString());
                        }
                        catch(Exception ex){output[f.Name+"_"+id]="Unavailable: "+ex.Message;}
                    }
                    Console.WriteLine(new JavaScriptSerializer().Serialize(output));
                }
                return 0;
            }
            catch(Exception ex){Console.Error.WriteLine(ex.Message);return 1;}
        }
    }
}
