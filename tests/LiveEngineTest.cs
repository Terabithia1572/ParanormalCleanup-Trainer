using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;

namespace ParanormalTrainer
{
    internal static class LiveEngineTest
    {
        public static int Main(string[] args)
        {
            var results=new List<object>();
            try
            {
                int pid;
                using(var engine=new TrainerEngine())
                {
                    engine.Attach();pid=engine.ProcessId;
                    for(int i=0;i<80&&!engine.IsReady;i++){Thread.Sleep(100);engine.Tick();}
                    if(!engine.IsReady)throw new Exception("Player not ready: "+engine.Status);
                    Console.WriteLine("LIVE: local player ready, PID "+pid);
                    var keys=new[]{"health","stamina","sprint","jump","revive","light","fov"};
                    var ids=new[]{108766,108759,108776,108857,108760,108774,108904};
                    for(int i=0;i<keys.Length;i++)
                    {
                        var f=engine.Find(ids[i]);string before=engine.Read(f),during,after;
                        try{engine.ToggleQuick(keys[i],true);engine.Tick();Thread.Sleep(100);engine.Tick();during=engine.Read(f);if(!engine.QuickEnabled(keys[i]))throw new Exception("Feature deactivated: "+engine.Status);}
                        finally{engine.ToggleQuick(keys[i],false);}
                        after=engine.Read(f);bool restored=after==before;
                        results.Add(new{key=keys[i],id=ids[i],before=before,active=during,after=after,restored=restored});
                        Console.WriteLine("LIVE: "+keys[i]+" "+before+" -> "+during+" -> "+after);
                        if(!restored)throw new Exception("Original value changed during test: "+keys[i]);
                    }
                    int readable=engine.Features.Count(f=>engine.Read(f)!="—");
                    Console.WriteLine("LIVE: "+readable+"/138 fields readable");
                    results.Add(new{readableFeatures=readable,totalFeatures=138});
                    engine.Detach();if(engine.IsAttached)throw new Exception("Detach failed");
                }
                var game=Process.GetProcessById(pid);byte[] file=File.ReadAllBytes(game.MainModule.FileName);int offset=TrainerEngine.FindPattern(file,"4C ?? ?? 4C ?? ?? ?? ?? 48 8B ?? 48 8B ?? 41 FF ?? ?? ?? ?? ?? 48 83").Single();
                using(var memory=new RemoteMemory(pid,true))
                {
                    long location=game.MainModule.BaseAddress.ToInt64()+TrainerEngine.FileOffsetToRva(file,offset);
                    if(!memory.Read(location,8).SequenceEqual(file.Skip(offset).Take(8)))throw new Exception("Code not restored after test");
                }
                Console.WriteLine("LIVE: original code restored; game remains running");
                File.WriteAllText(args[0],new JavaScriptSerializer().Serialize(new{status="passed",utc=DateTime.UtcNow.ToString("o"),scope="live memory read/write/restore, not a complete gameplay or level-transition test",results=results,originalCodeRestored=true}));
                return 0;
            }
            catch(Exception ex){Console.Error.WriteLine(ex);File.WriteAllText(args[0],new JavaScriptSerializer().Serialize(new{status="failed",error=ex.ToString(),results=results}));return 1;}
        }
    }
}
