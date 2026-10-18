using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ParanormalTrainer
{
    internal sealed class UeReflection
    {
        private readonly RemoteMemory memory;
        private readonly long blocks;
        private readonly Dictionary<long, Dictionary<string, Property>> cache = new Dictionary<long, Dictionary<string, Property>>();
        private sealed class Property { public int Offset, Size; public string Type, Owner; public byte ByteOffset, Mask; }
        internal UeReflection(RemoteMemory memory, long image) { this.memory=memory; blocks=image+0x4802F90; }
        private string Name(uint id)
        {
            if ((id>>16)>8191) throw new IOException("Geçersiz özellik adı.");
            long entry=memory.Pointer(blocks+(id>>16)*8)+(id&65535)*2;
            ushort header=BitConverter.ToUInt16(memory.Read(entry,2),0);int length=header>>6;
            if(length==0||length>1024)throw new IOException("Özellik adı doğrulanamadı.");
            return (header&1)==0?Encoding.UTF8.GetString(memory.Read(entry+2,length)):Encoding.Unicode.GetString(memory.Read(entry+2,length*2));
        }
        private string ObjectName(long obj) { return Name(BitConverter.ToUInt32(memory.Read(obj+0x18,4),0)); }
        public void RequirePlayer(long player)
        {
            long type=memory.Pointer(player+0x10);
            if(ObjectName(type)!="BP_PlayerCharacter_C")throw new IOException("Yakalanan nesne oyuncu karakteri değil.");
            long controller=memory.Pointer(player+0x258);
            if(ObjectName(memory.Pointer(controller+0x10))!="BP_PlayerController_Match_C" || memory.Pointer(controller+0x600)!=player)
                throw new IOException("Yerel oyuncu/denetleyici ilişkisi doğrulanamadı.");
        }
        public void RequireProperty(long owner, Feature feature)
        {
            long type=memory.Pointer(owner+0x10);Dictionary<string,Property> properties;
            if(!cache.TryGetValue(type,out properties)) {properties=Describe(type);cache.Add(type,properties);}
            Property property;
            if(!properties.TryGetValue(feature.DeclaringClass+"."+feature.PropertyName,out property) || property.Offset!=feature.PropertyOffset || property.Type!=feature.PropertyType)
                throw new IOException("Bu nesnede özellik eşleşmiyor: "+feature.TurkishName);
            if(feature.Writable && property.Type=="BoolProperty" && (property.Mask!=255||property.ByteOffset!=0))
                throw new IOException("Paylaşılan bit alanına yazma desteklenmiyor.");
            if(feature.Writable && feature.ComponentOffset+ (feature.Type=="Byte"?1:4)>property.Size)
                throw new IOException("Özellik boyutu doğrulanamadı.");
        }
        private Dictionary<string,Property> Describe(long cls)
        {
            var result=new Dictionary<string,Property>();var classes=new HashSet<long>();
            while(cls!=0 && classes.Add(cls) && classes.Count<=32)
            {
                string owner=ObjectName(cls);long field=BitConverter.ToInt64(memory.Read(cls+0x50,8),0);var fields=new HashSet<long>();
                while(field!=0 && fields.Add(field) && fields.Count<=1024)
                {
                    byte[] data=memory.Read(field,0x80);string name=Name(BitConverter.ToUInt32(data,0x28));long fieldClass=BitConverter.ToInt64(data,8);
                    string type=Name(BitConverter.ToUInt32(memory.Read(fieldClass,4),0));
                    var p=new Property {Offset=BitConverter.ToInt32(data,0x4c),Size=BitConverter.ToInt32(data,0x3c),Owner=owner,Type=type,ByteOffset=data[0x79],Mask=data[0x7b]};
                    result[owner+"."+name]=p;field=BitConverter.ToInt64(data,0x20);
                }
                cls=BitConverter.ToInt64(memory.Read(cls+0x40,8),0);
            }
            return result;
        }
    }
}
