import {cpSync, existsSync, mkdirSync, mkdtempSync, readFileSync, writeFileSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {dirname, join, resolve} from 'node:path';
import {fileURLToPath} from 'node:url';
import {createRequire} from 'node:module';

const source = resolve(process.argv[2]);
const here = dirname(fileURLToPath(import.meta.url));
const project = resolve(here, '../..');
const stage = mkdtempSync(join(tmpdir(), 'photon-client-'));
const src = join(stage, 'src');
const require = createRequire(join(source, 'package.json'));
const {build} = require('esbuild');
const destination = join(project, 'Assets/StreamingAssets/PhotonClient');
const read = path => readFileSync(path, 'utf8');
const replace = (text, begin, end, next) => {
  const first = text.indexOf(begin), last = text.indexOf(end, first + begin.length);
  if (first < 0 || last < 0) throw new Error('Reference changed at ' + begin);
  return text.slice(0, first) + next + text.slice(last);
};

cpSync(join(source, 'src'), src, {recursive: true, filter:path=>!path.endsWith('.test.js')});
cpSync(join(source, 'assets/models'), join(stage, 'assets/models'), {recursive: true});
cpSync(join(source, 'assets/covers/photon-fox-cover.png'), join(stage, 'assets/covers/photon-fox-cover.png'));
cpSync(join(source, 'public/tuning.json'), join(src, 'tuning-defaults.json'));
for (const name of ['transport.js', 'unity.css']) cpSync(join(here, name), join(src, name));
const edit = (file, transform) => writeFileSync(join(src, file), transform(read(join(src, file))));

edit('car-visual.js', text => {
  for (const car of ['balanced', 'agile', 'stable', 'burst']) {
    text = "import " + car + " from '../assets/models/" + car + ".glb';\n" +
      text.replace("new URL('../assets/models/" + car + ".glb', import.meta.url)", 'new URL(' + car + ')');
  }
  return text;
});
edit('tuning.js', text => "import tuningDefaults from './tuning-defaults.json';\n" +
  replace(text, '    const response = await fetch', '    if (file.schemaVersion', '    const file = tuningDefaults;\n'));
edit('start.js', text => "import './unity.css';\n" + text.replace('    console.error(error);',
  "    window.webkit?.messageHandlers.photon.postMessage({type:'error', message:String(error.stack || error)});\n    console.error(error);"));
edit('main.js', text => {
  // Keep the reference's gameplay, input, CSS/HUD and room protocol. Unity
  // takes the live camera and scene poses, and renders imported meshes itself.
  text = text.replace(/^import .*from ['"][^'"]*(?:-scene|map-lighting|boost-exhaust|airflow|drift-fire|debug-panel)\.js['"];?\r?\n/gm, '');
  text = "import {createTransport, nativeMessage, setupNativeFullscreen} from './transport.js';\n" + text;
  text = text.replace("const vehicleStorageKey=", `
window.addEventListener('error', event => {
 if (event.error) nativeMessage({type:'error',message:String(event.error.stack || event.error)});
});
window.addEventListener('unhandledrejection', event => {
 nativeMessage({type:'error',message:String(event.reason?.stack || event.reason)});
});
const vehicleStorageKey=`);
  text = text.replace("setupFullscreen($('app'),$('fullscreen-toggle'),$('fullscreen-status'));",
    "setupNativeFullscreen($('fullscreen-toggle'));");
  text = replace(text, 'const renderer = ', 'const routeNearest=', `
const vehicleLighting={follow(){}};
const mapLighting={shadeVehicle(){}};
const colorGrading={setBlurRadius(){}};
let speedBlurRadius=0;
`);
  text = replace(text, " if(id===MIAO_ID&&!miaoVillageScene)", ' if(activeMap!==id){', '');
  text = replace(text, ' activeMap=id;if(miaoVillageScene)', '\n}\nlet useItem=', ' activeMap=id;');
  text = text.replace('const boostExhaust=createBoostExhaust(scene,car);\nconst airflow=createAirflow(scene,car);',
    'const boostExhaust={update(){}}; const airflow={clear(){},update(){}};');
  text = text.replace('const driftFire=createDriftFire(scene,getVehicle(selectedVehicleId).color);',
    'const driftFire={clear(){},setTheme(){},emit(){},update(){}};');
  text = text.replace('renderer.setSize(w,h);colorGrading.resize();', '');
  text = text.replace('  debugPanel.beginGpu();\n  colorGrading.render();\n  debugPanel.endGpu();', '  publishFrame();');
  text = text.replace('  if(debugPanel.visible)debugPanel.update(performance.now()-cpuStart,now);', '');
  text = text.replace('  const cpuStart=debugPanel.visible?performance.now():0;', '');
  text = text.replace(' const socket=new WebSocket(`${location.protocol===\'https:\'?\'wss\':\'ws\'}://${roomHost}/room`);',
    ' const socket=createTransport({spawn: map => map===\'stadium\'?stadiumSlot(0):null});');
  text = text.replace('STADIUM,stadiumContact,createStadium','STADIUM,stadiumContact,stadiumSlot');
  text = text.replace("r={model,shade,flames:visual.flames,", "r={model,shade,paint:p.color,flames:visual.flames,");
  text = text.replace("  if(!e.repeat&&e.code==='KeyY'){e.preventDefault();debugPanel.toggle();}", "");
  text = text.replace("textContent='比赛排行榜';$('splits').textContent='';",
    "textContent=m.roomId==='offline'?format(race.elapsed):'比赛排行榜';$('splits').textContent=m.roomId==='offline'&&activeMap!=='akina'?race.splits.map((s,i)=>`第 ${i+1} 圈  ${format(s)}`).join(' · '):'';");
  text = text.replace("...m.ranking.map((p,i)=>", "...(m.roomId==='offline'?[]:m.ranking).map((p,i)=>");
  text = text.replace("pregameUI.setRoom(m,id,spectating);", "pregameUI.setRoom(m,id,spectating);socket.updateView?.(m);");
  text = text.replace("const remote=new Map();let id=", "const remote=new Map();remoteCars=remote;let id=");
  text = text.replace("let last=performance.now()", `
let remoteCars;
const pose = (o,id,vehicle,boost,color) => {
 const q=o.quaternion, p=o.position;
 return {id,vehicle,color,boost,visible:o.visible,x:p.x,y:p.y,z:-p.z,qx:-q.x,qy:-q.y,qz:q.z,qw:q.w,scale:o.scale.x};
};
let priorSkid=-1, priorMap, lastStateTime=0;
function publishFrame(){
 const cars=[pose(car,'local',selectedVehicleId,driver.nitroTime>0?(driver.miniBoost>0?3:2):driver.miniBoost>0?1:0,visualKey.slice(selectedVehicleId.length))];
 for(const [id,remote] of remoteCars||[])cars.push(pose(remote.model,id,remote.vehicleId,remote.flames.some(f=>f.visible)?2:0,remote.paint));
 const marks=[];
 if(skidIndex!==priorSkid||priorMap!==activeMap){
  for(let i=(priorSkid<0||priorMap!==activeMap||skidIndex<priorSkid?0:priorSkid); i<skidIndex; i++)
   marks.push(...skidPositions.subarray(i*18,i*18+18));
 }
 const clearMarks=priorSkid<0||priorMap!==activeMap||skidIndex<priorSkid;
 priorSkid=skidIndex;priorMap=activeMap;
 const cameraPose=pose(camera,'','','',null);
 nativeMessage({type:'frame',map:activeMap,mode,fov:camera.fov,camera:cameraPose,cars,marks,clearMarks,effects:itemView.worldEffects(),drifting:driver.drifting&&driver.speed>10,frames:++lastStateTime});
}
let last=performance.now()`);
  // Send the item shapes separately so they can be built out of native meshes.
  const pos = text.indexOf('\nif(testMode){\n  let touchSlideReplay');
  if (pos < 0) throw new Error('Reference playtest section changed');
  return text.slice(0, pos);
});
edit('pregame-ui.js', text => text.replace('使用浏览器原生全屏', '切换游戏全屏或最大化游戏窗口'));
edit('item-view.js', text => text.replace(' snapshot(){', `
 worldEffects(){
  const objects=[];
  const add=(id,type,o)=>{if(o.visible)objects.push({id,type,x:o.position.x,y:o.position.y,z:-o.position.z,yaw:-o.rotation.y,scale:o.scale.x});};
  if(root.visible){boxes.forEach((o,i)=>add('box'+i,'box',o));for(const [id,o] of hazardMeshes)add(id,o.userData.hazardType,o);for(const [id,o] of projectiles)add(id,'projectile',o);}
  for(const [id,o] of Object.entries({shield,ufo,trap,impact}))add(id,id,o);
  return objects;
 },
 snapshot(){`));
const output = await build({
  entryPoints: [join(src, 'start.js')], outdir: stage, entryNames: 'photon',
  write: false, bundle: true, format: 'iife', target: 'safari17', legalComments: 'inline',
  loader: {'.glb':'dataurl','.png':'dataurl'}, nodePaths: [join(source, 'node_modules')],
  define: {'import.meta.env.BASE_URL': '"./"', 'import.meta.env.DEV': 'false'},
});
const css = output.outputFiles.find(file => file.path.endsWith('.css'));
const script = output.outputFiles.find(file => file.path.endsWith('.js'));
const html = read(join(source, 'index.html')).replaceAll('src="/nitro-icon.png"', 'src="./nitro-icon.png"');
if (!html.includes('<script type="module" src="/src/start.js"></script>')) throw new Error('Reference entry changed');
mkdirSync(destination, {recursive:true});
writeFileSync(join(destination, 'index.html'), html
  .replace('</head>', '<link rel="stylesheet" href="./photon.css"></head>')
  .replace('<script type="module" src="/src/start.js"></script>', () => '<script>'+script.text.replaceAll(/<\/script/gi, '<\\/script')+'</script>'));
writeFileSync(join(destination, 'photon.css'), css.text);
for(const name of ['ui','items','item-fox.png','nitro-icon.png','akina-route.svg','full-city-route.svg']) {
  const path = join(source, 'public', name);
  if (existsSync(path)) cpSync(path, join(destination, name), {recursive:true});
}
console.log('Built Unity interface in', destination);
