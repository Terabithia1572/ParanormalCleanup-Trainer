using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace ParanormalTrainer
{
    internal sealed class UeProbe
    {
        private RemoteMemory m; private long blocks;
        private string Name(uint id)
        {
            if (id >> 16 > 8191) return "INVALID";
            try { long block = m.Pointer(blocks + (id >> 16) * 8); long entry = block + (id & 65535) * 2;
                ushort header = BitConverter.ToUInt16(m.Read(entry,2),0); int length=header>>6;
                if(length>1024 || length==0) return "INVALID";
                return (header&1)==0?Encoding.UTF8.GetString(m.Read(entry+2,length)):Encoding.Unicode.GetString(m.Read(entry+2,length*2));
            }catch{return "INVALID";}
        }
        private string ObjectName(long obj) { return Name(BitConverter.ToUInt32(m.Read(obj+0x18,4),0)); }
        private object Describe(long obj)
        {
            var result=new Dictionary<string,object>();result["address"]=obj.ToString("X");result["name"]=ObjectName(obj);
            var classes=new List<object>();long cls=m.Pointer(obj+0x10);
            for(int n=0;n<20 && cls!=0;n++)
            {
                var properties=new List<object>();long f=BitConverter.ToInt64(m.Read(cls+0x50,8),0); var seen=new HashSet<long>();
                while(f>0x10000&&seen.Add(f)&&seen.Count<500)
                {
                    byte[] bytes=m.Read(f,0x80);string name=Name(BitConverter.ToUInt32(bytes,0x28));int offset=BitConverter.ToInt32(bytes,0x4c),size=BitConverter.ToInt32(bytes,0x3c);
                    long type=BitConverter.ToInt64(bytes,8);string typeName=Name(BitConverter.ToUInt32(m.Read(type,4),0));
                    string value="";try{var data=m.Read(obj+offset,Math.Min(16,Math.Max(size,4)));value=BitConverter.ToString(data);}catch{}
                    properties.Add(new{ Name=name,Type=typeName,Offset=offset,Size=size,Value=value,BoolMetadata=BitConverter.ToString(bytes.Skip(0x70).Take(8).ToArray()),MetadataAddress=f.ToString("X")});
                    f=BitConverter.ToInt64(bytes,0x20);
                }
                classes.Add(new{Name=ObjectName(cls),Address=cls.ToString("X"),Properties=properties});cls=BitConverter.ToInt64(m.Read(cls+0x40,8),0);
            }
            result["classes"]=classes; return result;
        }
        public static int Main(string[] args)
        {
            try {return new UeProbe().Run(args);}catch(Exception e){Console.Error.WriteLine(e);return 1;}
        }
        private int Run(string[] args)
        {
            var process=Process.GetProcessesByName(TrainerEngine.TargetName).Single();long image=process.MainModule.BaseAddress.ToInt64();
            byte[] file=File.ReadAllBytes(process.MainModule.FileName);if(TrainerEngine.Hash(file)!=TrainerEngine.SupportedSha256)throw new Exception("Unsupported image");
            long origin=image+TrainerEngine.FileOffsetToRva(file,TrainerEngine.FindPattern(file,"4C ?? ?? 4C ?? ?? ?? ?? 48 8B ?? 48 8B ?? 41 FF ?? ?? ?? ?? ?? 48 83").Single());
            using(m=new RemoteMemory(process.Id,true))
            {
                var patch=m.Read(origin,8);if(patch[0]!=0xe9)throw new Exception("Trainer hook absent");long cave=origin+5+BitConverter.ToInt32(patch,1)-16;long pawn=m.Pointer(cave);
                byte[] globals=m.Read(image+70692864,6795620); var visited=new HashSet<long>();
                for(int i=0;i<globals.Length-8;i+=8)
                {
                    long ptr=BitConverter.ToInt64(globals,i);if(ptr<0x100000000L||ptr>=image||(ptr&15)!=0||!visited.Add(ptr))continue;
                    try{byte[] h=m.Read(ptr,16);if((BitConverter.ToUInt16(h,0)>>6)==4&&h[2]==78&&h[3]==111&&h[4]==110&&h[5]==101){blocks=image+70692864+i;break;}}catch{}
                }
                if(blocks==0)throw new Exception("Name blocks not found, candidates "+visited.Count);
                var objects=new Dictionary<string,object>();objects["player"]=Describe(pawn);
                long controller=m.Pointer(pawn+0x258);objects["controller"]=Describe(controller);
                long local=m.Pointer(controller+0x298),viewport=m.Pointer(local+0x70),instance=m.Pointer(viewport+0x80);objects["instance"]=Describe(instance);
                foreach(long off in new long[]{0x530,0x5d0,0x610,0x288})try{objects["player+"+off.ToString("X")]=Describe(m.Pointer(pawn+off));}catch(Exception ex){objects["player+"+off.ToString("X")]=ex.Message;}
                try{objects["save"]=Describe(m.Pointer(instance+0x240));}catch(Exception ex){objects["save"]=ex.Message;}
                try{objects["worldSettings"]=Describe(m.Pointer(m.Pointer(m.Pointer(viewport+0x78)+0x30)+0x258));}catch(Exception ex){objects["worldSettings"]=ex.Message;}
                var result=new{Pid=process.Id,Image=image.ToString("X"),NameBlocks=blocks.ToString("X"),NameBlocksRva=(blocks-image).ToString("X"),ReadOnly=true,Objects=objects};
                var json=new JavaScriptSerializer{MaxJsonLength=16000000}.Serialize(result);File.WriteAllText(args[0],json);Console.WriteLine("Wrote "+args[0]+" with "+json.Length+" bytes; NameBlocks RVA="+(blocks-image).ToString("X"));
            }
            return 0;
        }
    }
}
