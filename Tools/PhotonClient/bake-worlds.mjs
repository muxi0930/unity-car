import {createRequire} from 'node:module';
import {existsSync, mkdirSync, mkdtempSync, readFileSync, writeFileSync} from 'node:fs';
import {join, resolve} from 'node:path';
import {pathToFileURL} from 'node:url';
import {tmpdir} from 'node:os';
import {gzipSync} from 'node:zlib';
import {createHash} from 'node:crypto';

const reference = resolve(process.argv[2]);
const canvasModule = process.argv[3] || '@napi-rs/canvas';
const require = createRequire(join(reference, 'package.json'));
const {createCanvas, loadImage, ImageData} = require(canvasModule);
const {build} = require('esbuild');
const folder = resolve('Assets/Resources/Photon');
const scratch = mkdtempSync(join(tmpdir(), 'photon-worlds-'));
const entry = join(scratch, 'entry.mjs');
const output = join(scratch, 'worlds.mjs');
const three = join(reference,'node_modules/three/build/three.module.js');
const worlds = [
  ['city','city-scene','createCity'], ['city-full','full-city-scene','createFullCity'],
  ['akina','akina-scene','createAkina'], ['tangong','tangong-scene','createTangong'],
  ['miao-village','miao-village-scene','createMiaoVillage'], ['ice-valley','ice-valley-scene','createIceValley'],
  ['snow','snow-scene','createSnow'], ['seaside','seaside-scene','createSeaside'],
  ['stadium','stadium','createStadium'],
];
const importFrom = (symbol, path) => `import {${symbol}} from ${JSON.stringify(path)};`;
writeFileSync(entry, [
  `import * as THREE from ${JSON.stringify(three)}; export {THREE};`,
  importFrom('GLTFLoader', join(reference,'node_modules/three/examples/jsm/loaders/GLTFLoader.js')),
  importFrom('MAP_LIGHTING', join(reference,'src/map-lighting-config.js')),
  ...worlds.map(([,file,fn])=>importFrom(fn,join(reference,'src',file+'.js'))),
  'export {GLTFLoader, MAP_LIGHTING};',
  `export const worlds = {${worlds.map(([id,,fn])=>JSON.stringify(id)+':'+fn).join(',')}};`,
].join('\n'));
await build({ entryPoints:[entry], outfile:output, bundle:true, platform:'node', format:'esm',
  define:{'import.meta.env.BASE_URL':'"./"'}, nodePaths:[join(reference,'node_modules')] });
const {THREE, GLTFLoader, MAP_LIGHTING, worlds:constructors} = await import(pathToFileURL(output));
globalThis.document = {createElement: name => {
  if (name !== 'canvas') throw new Error('Unexpected element '+name);
  return createCanvas(16, 16);
}};
const publicAsset = path => join(reference, 'public', path);
const textureNames = new Map();
const preloaded = new Map();
for (const file of ['ice-valley/ice.webp','ice-valley/slate.webp','ice-valley/sky.webp','miao-village/horizon.webp']) {
  preloaded.set(file, await loadImage(publicAsset(file)));
}
THREE.TextureLoader.prototype.load = function(url, ready) {
  const image = preloaded.get(url.replace(/^\.\//, ''));
  if (!image) throw new Error('Texture missing '+url);
  const texture = new THREE.Texture(image);
  ready?.(texture);
  return texture;
};

function storeTexture(image, stableName) {
  const key = createHash('sha256').update(image).digest('hex').slice(0,16);
  const name = stableName || 'tex-'+key;
  const path = join(folder,'Textures',name+'.png');
  if (!existsSync(path) || stableName) writeFileSync(path,image);
  return name;
}
function pngFor(image) {
  if (image.toBuffer) return image.toBuffer('image/png');
  const {width, height} = image;
  const canvas=createCanvas(width,height), ctx=canvas.getContext('2d');
  if (image.data) ctx.putImageData(new ImageData(Uint8ClampedArray.from(image.data),width,height),0,0);
  else ctx.drawImage(image,0,0);
  return canvas.toBuffer('image/png');
}
function textureName(texture) {
  if (!texture?.image?.width) return '';
  if (!textureNames.has(texture)) textureNames.set(texture,storeTexture(pngFor(texture.image)));
  return textureNames.get(texture);
}

function bake(root, file, isCar=false) {
  root.updateMatrixWorld(true);
  const batches = new Map();
  root.traverseVisible(mesh => {
    if (!mesh.isMesh && !mesh.isLine) return;
    const geo=mesh.geometry, attr=geo.attributes;
    const groups=Array.isArray(mesh.material)?geo.groups:[{start:0,count:geo.index?.count||attr.position.count,materialIndex:0}];
    for(const group of groups) {
      const material=Array.isArray(mesh.material)?mesh.material[group.materialIndex]:mesh.material;
      if(!material||material.opacity===0)continue;
      const color=material.color?.clone()||new THREE.Color(1,1,1);
      if(isCar && file.endsWith('burst.foxmesh') && material.name==='burst pearlescent paint')color.setHex(0x9257ef);
      const metallic=material.name?.endsWith('pearlescent paint')?.34:(material.metalness||0);
      const smoothness=material.name?.endsWith('pearlescent paint')?.77:1-(material.roughness??1);
      const tex=textureName(material.map);
      const flags=(material.isMeshBasicMaterial||mesh.isLine?1:0)|(material.side===THREE.DoubleSide?2:0)|
        (material.transparent?4:0)|(mesh.isLine?8:0)|(material.name==='driver_identity'?16:0)|
        (material.map?.wrapS===THREE.RepeatWrapping?32:0);
      const hasColor=!!(material.vertexColors&&attr.color)||!!mesh.instanceColor;
      const partition=isCar?'':Math.floor(mesh.matrixWorld.elements[12]/160)+','+Math.floor(mesh.matrixWorld.elements[14]/160);
      const key=JSON.stringify([material.name,color.getHex(),metallic,smoothness,tex,flags,hasColor,material.opacity,partition]);
      const batch=batches.get(key)||{name:material.name||mesh.name||'Surface '+batches.size,
        color:color.convertLinearToSRGB(),metallic,smoothness,tex,flags,hasColor,opacity:material.opacity,
        p:[],n:[],uv:[],c:[],idx:[]};
      batches.set(key,batch);
      const splitGuardrail=file==='Tracks/city.foxmesh' &&
        (material.name==='city-guardrail-steel' || material.name==='city-guardrail-bolts');
      const chunks=splitGuardrail?(batch.chunks??=new Map()):null;
      const basis=new THREE.Matrix4(), instance=new THREE.Matrix4(), normal=new THREE.Matrix3();
      const point=new THREE.Vector3(), direction=new THREE.Vector3(), instanceColor=new THREE.Color();
      for(let row=0;row<(mesh.isInstancedMesh?mesh.count:1);row++){
        basis.copy(mesh.matrixWorld);
        if(mesh.isInstancedMesh){mesh.getMatrixAt(row,instance);basis.multiply(instance);}
        let target=batch;
        if(chunks){
          // Partition whole instances by their position in the exported Unity coordinates.
          const cell=Math.floor(basis.elements[12]/128)+','+Math.floor(-basis.elements[14]/128);
          if(!chunks.has(cell)){
            const {name,color,metallic,smoothness,tex,flags,hasColor,opacity}=batch;
            chunks.set(cell,{name,color,metallic,smoothness,tex,flags,hasColor,opacity,p:[],n:[],uv:[],c:[],idx:[]});
          }
          target=chunks.get(cell);
        }
        if(mesh.instanceColor)mesh.getColorAt(row,instanceColor);
        normal.getNormalMatrix(basis);
        const indices=new Map();
        for(let k=0;k<group.count;k++){
          const source=geo.index?geo.index.getX(group.start+k):group.start+k;
          let index=indices.get(source);
          if(index===undefined){
            index=target.p.length/3;indices.set(source,index);
            point.fromBufferAttribute(attr.position,source).applyMatrix4(basis);
            if(attr.normal)direction.fromBufferAttribute(attr.normal,source).applyMatrix3(normal).normalize();
            else direction.set(0,1,0);
            target.p.push(point.x,point.y,-point.z);
            target.n.push(direction.x,direction.y,-direction.z);
            const uv=new THREE.Vector2(attr.uv?.getX(source)||0,attr.uv?.getY(source)||0);
            if(material.map?.matrixAutoUpdate)material.map.updateMatrix();
            if(material.map)uv.applyMatrix3(material.map.matrix);
            target.uv.push(uv.x,material.map?.flipY===false?1-uv.y:uv.y);
            if(hasColor){
              const c=material.vertexColors&&attr.color?new THREE.Color().fromBufferAttribute(attr.color,source):new THREE.Color(1,1,1);
              if(mesh.instanceColor)c.multiply(instanceColor);
              target.c.push(...[c.r,c.g,c.b].map(value=>Math.min(255,Math.round(255*value))),255);
            }
          }
          target.idx.push(index);
          if(mesh.isLine && !mesh.isLineSegments && k>0 && k<group.count-1)target.idx.push(index);
        }
      }
    }
  });
  const parts=[];
  const int = n => {const b=Buffer.allocUnsafe(4);b.writeInt32LE(n);parts.push(b);};
  const str = value => {const bytes=Buffer.from(value);int(bytes.length);parts.push(bytes);};
  const floats = values => parts.push(Buffer.from(Float32Array.from(values).buffer));
  const outputBatches=[...batches.values()].flatMap(batch=>batch.chunks?[...batch.chunks.values()]:[batch]);
  int(-2);int(outputBatches.length);
  for(const b of outputBatches){
    str(b.name); floats([b.color.r,b.color.g,b.color.b,b.metallic,b.smoothness,b.opacity]);
    str(b.tex);int(b.flags);int(b.hasColor?1:0);
    int(b.p.length/3);floats(b.p);floats(b.n);floats(b.uv);if(b.hasColor)parts.push(Buffer.from(b.c));
    int(b.idx.length);parts.push(Buffer.from(Int32Array.from(b.idx).buffer));
  }
  writeFileSync(join(folder,file), gzipSync(Buffer.concat(parts),{level:6}));
  console.log(file,outputBatches.length,'batches');
}

mkdirSync(join(folder,'Textures'),{recursive:true});
for(const sub of ['Tracks','Cars'])mkdirSync(join(folder,sub),{recursive:true});
const mat=(color,extra={})=>new THREE.MeshStandardMaterial({color,roughness:.8,...extra});
for(const [id, make] of Object.entries(constructors)){
  const argument=id==='city'?{mat,blue:mat(0x239cbb),white:mat(0xf7f6e5)}:()=>{};
  bake(make(argument),'Tracks/'+id+'.foxmesh');
}
for(const id of ['balanced','agile','stable','burst']){
  const model=readFileSync(join(reference,'assets/models',id+'.glb'));
  const data=model.buffer.slice(model.byteOffset, model.byteOffset+model.byteLength);
  bake((await new GLTFLoader().parseAsync(data,'')).scene,'Cars/'+id+'.foxmesh',true);
}
const skySources={
  city:'city-atmosphere-sky.jpg',akina:'akina-atmosphere-sky.jpg',
  'miao-village':'miao-village/horizon.webp','ice-valley':'ice-valley/sky.webp',
};
for(const [id,path] of Object.entries(skySources))storeTexture(pngFor(await loadImage(publicAsset(path))),'sky-'+id);
const illustration=await loadImage(publicAsset('tangong-sky.jpg'));
const panoramic=createCanvas(4096,2048), ctx=panoramic.getContext('2d');
const gradient=ctx.createLinearGradient(0,0,0,2048);
for(const [position,color] of [[0,'#20a9e9'],[.42,'#9bd9ee'],[.6,'#b8e4ed'],[1,'#2fa6c4']])gradient.addColorStop(position,color);
ctx.fillStyle=gradient;ctx.fillRect(0,0,4096,2048);
for(let i=0;i<4;i++){
  ctx.save();ctx.translate(i*1024,0);
  if(i%2){ctx.translate(1024,0);ctx.scale(-1,1);}
  ctx.drawImage(illustration,0,790,1024,427);ctx.restore();
}
const fade=ctx.createLinearGradient(0,790,0,850);
fade.addColorStop(0,'#9bd9ee');fade.addColorStop(1,'#9bd9ee00');
ctx.fillStyle=fade;ctx.fillRect(0,790,4096,60);
storeTexture(pngFor(panoramic),'sky-tangong');
storeTexture(pngFor(await loadImage(publicAsset('item-fox.png'))),'item-box');
storeTexture(pngFor(await loadImage(publicAsset('items/banana.svg'))),'item-banana');
writeFileSync(join(folder,'lighting.json'),JSON.stringify({maps:Object.entries(MAP_LIGHTING).map(([id,config])=>({id,...config}))}));
