import fs from 'node:fs';
import assert from 'node:assert/strict';
const runtime = JSON.parse(fs.readFileSync(new URL('../analysis/live-metadata.json', import.meta.url)));
const features = JSON.parse(fs.readFileSync(new URL('../src/features.json', import.meta.url)));
const original = JSON.parse(fs.readFileSync(new URL('../analysis/ct-features.json', import.meta.url)));
const normalize = s => s.toLowerCase().replace(/[^a-z0-9]/g, '');
const aliases = {108766:'Health',108768:'CameraLocation',108769:'CameraLocation',108770:'CameraLocation',108857:'JumpZVelocity',108858:'MaxWalkSpeed',108859:'MaxWalkSpeedCrouched',108773:'PlayerName',108860:'EXPGainedThisMatch',108894:'SessionEXPRewardOnWIn',108905:'PrestigeOverflowEXP',108883:'PrestigeOverflowEXP'};
const changes=[];
for(const f of features) {
  const source=original.features.find(s=>s.id===f.Id), c=[...source.playerChain];
  const key=c.length===1||f.Id===108773?'player':c[0]===0x510?'player+530':c[0]===0x5b0?'player+5D0':c[0]===0x5f0?'player+610':c[0]===0x288?'player+288':f.Id===108907?'worldSettings':c.length===2?'controller':c.length===6?'save':'instance';
  const obj=runtime.Objects[key];assert.ok(obj?.classes,`Missing object ${key}`);
  const matches=obj.classes.flatMap(cl=>cl.Properties.map(p=>({...p,DeclaringClass:cl.Name}))).filter(p=>normalize(p.Name)===normalize(aliases[f.Id]||f.Name));
  assert.equal(matches.length,1,`Property resolution ${f.Id} ${f.Name}`);const p=matches[0];
  let owner=c.slice(0,-1);
  if(c[0]===0x510)owner[0]=0x530;
  if(c[0]===0x5b0)owner[0]=0x5d0;
  if(c[0]===0x5f0)owner[0]=0x610;
  const delta=f.Id===108769?4:f.Id===108770?8:0;
  if(f.Id===108773) { owner=[];f.Chain=[p.Offset,0]; } else f.Chain=[...owner,p.Offset+delta];
  f.OwnerChain=owner;f.PropertyName=p.Name;f.DeclaringClass=p.DeclaringClass;f.PropertyType=p.Type;f.PropertyOffset=p.Offset;f.ComponentOffset=delta;
  const expected=f.Type==='Float'?'FloatProperty':f.Type==='4 Bytes'?'IntProperty':f.Type==='Byte'?['BoolProperty','ByteProperty']: 'StrProperty';
  if(![108768,108769,108770,108814,108815].includes(f.Id))assert.ok([expected].flat().includes(p.Type),`Type mismatch ${f.Id}: ${f.Type} / ${p.Type}`);
  if(JSON.stringify(c)!==JSON.stringify(f.Chain))changes.push({id:f.Id,name:f.Name,old:c.map(v=>v.toString(16)),updated:f.Chain.map(v=>v.toString(16)),property:p.Name,declaringClass:p.DeclaringClass});
}
fs.writeFileSync(new URL('../src/features.json',import.meta.url),JSON.stringify(features,null,2)+'\n');
fs.writeFileSync(new URL('../analysis/profile-adjustments.json',import.meta.url),JSON.stringify({source:'read-only live UE property metadata',gameSha256:'9a06f5834c8a7593e05ec92de6bfeec6cb975bae63a303a9615fbab006fbcc9e',featuresVerified:features.length,changed:changes.length,nameBlocksRva:runtime.NameBlocksRva,changes},null,2)+'\n');
console.log(`${features.length} properties verified; ${changes.length} pointer chains corrected.`);
