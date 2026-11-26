export function nativeMessage(message) {
  window.webkit?.messageHandlers.photon.postMessage(message);
}

export function setupNativeFullscreen(button) {
  button.onclick = () => nativeMessage({type:'fullscreen'});
  window.photonFullscreenChanged = enabled => {
    button.textContent = enabled ? '退出全屏' : '全屏';
    button.setAttribute('aria-pressed', String(enabled));
  };
}

// A local time trial uses the same UI events, but has no socket or room server.
function offlineSession(emit, spawn) {
  const id = 'offline-player', roomId = 'offline';
  let state, start;
  let boards = {};
  try { boards = JSON.parse(localStorage.getItem('photon-local-records') || '{}'); } catch {}
  const send = event => emit('message', {...event, now:Date.now()});
  const commit = () => send(state);
  const hall = () => {
    clearTimeout(start);
    state = null;
    send({type:'hall',rooms:[]});
  };
  return {
    open() {
      emit('open');
      send({type:'welcome',id});
      send({type:'leaderboard',boards});
      hall();
    },
    close() {clearTimeout(start);},
    send(message) {
      if (message.type === 'ping') {send({...message,type:'pong'});return;}
      if (message.type === 'create' && !state) {
        state={type:'room',roomId,name:message.roomName?.trim()||message.name+'的试跑',mapId:'city',
          phase:'lobby',gameMode:'race',host:id,round:0,startAt:0,endAt:0,ranking:[],
          players:[{id,name:message.name,vehicleId:message.vehicleId,connected:true,color:'#e9f6ff',number:1,racing:false,finish:null,lap:1,progress:0}]};
        commit();
        return;
      }
      if (!state) return;
      const self=state.players[0];
      switch(message.type) {
        case 'leave': hall(); break;
        case 'vehicle':
          if(state.phase==='lobby'){self.vehicleId=message.vehicleId;commit();}
          break;
        case 'map':
          if(state.phase==='lobby'){state.mapId=message.mapId;commit();}
          break;
        case 'start':
          if(state.phase!=='lobby')break;
          state.phase='countdown';state.round++;state.startAt=Date.now()+3000;
          self.racing=true;self.finish=null;self.grid=spawn(state.mapId);
          start=setTimeout(()=>{state.phase='racing';commit();},3000);
          commit();
          break;
        case 'finish':
          if(state.phase!=='racing'||message.round!==state.round)break;
          self.finish=message.elapsed;
          state.phase='results';
          state.ranking=[self];
          const previous=(boards[state.mapId]||[]).filter(record=>record.name!==self.name);
          const best=Math.min(message.elapsed, (boards[state.mapId]||[]).find(record=>record.name===self.name)?.elapsed??Infinity);
          boards[state.mapId]=[...previous,{name:self.name,elapsed:best}].sort((a,b)=>a.elapsed-b.elapsed).slice(0,3);
          try {localStorage.setItem('photon-local-records',JSON.stringify(boards));} catch {}
          send({type:'leaderboard',boards});
          commit();
          break;
        case 'return':
          if(state.phase!=='results')break;
          state.phase='lobby';self.racing=false;commit();
          break;
        case 'endPractice':
          if(state.mapId!=='stadium')break;
          clearTimeout(start);state.phase='lobby';self.racing=false;commit();
          break;
      }
    },
  };
}

export function createTransport({spawn}) {
  let session=0, offline, address='';
  const socket={readyState:0,bufferedAmount:0,send(raw) {
    if (offline) {const target=offline;queueMicrotask(()=>{if (offline===target) target.send(JSON.parse(raw));});}
    else nativeMessage({type:'send',session,data:raw});
  }};
  const emit=(type,data)=>{
    if(type==='open')socket.readyState=1;
    if(type==='close')socket.readyState=3;
    socket['on'+type]?.(type==='message'?{data:typeof data==='string'?data:JSON.stringify(data)}:undefined);
    syncLabels();
  };
  const bar=document.createElement('div');
  bar.className='photon-connection';
  bar.innerHTML='<button type="button" class="pg-button" data-local>单人试跑</button><button type="button" class="pg-button pg-quiet" data-online>局域网连接</button>';
  const createButton=document.querySelector('#open-create-room');
  createButton.before(bar);
  const dialog=document.createElement('dialog');
  dialog.className='pg-dialog photon-server-dialog';
  dialog.innerHTML='<form><strong>连接局域网房间服务</strong><label>服务器地址<input required placeholder="192.168.1.10:3000" autocomplete="off" spellcheck="false"></label><p role="status"></p><div class="photon-server-actions"><button type="button" class="pg-button pg-quiet" data-cancel>取消</button><button type="submit" class="pg-button pg-gold">连接</button></div></form>';
  document.querySelector('#pregame-ui').append(dialog);
  const online=bar.querySelector('[data-online]'),local=bar.querySelector('[data-local]');
  const input=dialog.querySelector('input'),error=dialog.querySelector('p');
  const status=()=>document.querySelector('#pg-connection');
  function syncLabels() {
    if(offline&&status())status().textContent='离线试跑';
    if(offline)document.querySelector('#open-create-room').textContent='创建单人试跑';
    else if(document.querySelector('#open-create-room')?.textContent==='创建单人试跑')document.querySelector('#open-create-room').textContent='创建房间 →';
    local.hidden=!!offline;
    online.textContent=offline?'局域网连接':socket.readyState===1?'切换服务器':'连接服务器';
  }
  const startOffline=()=>{
    ++session;
    nativeMessage({type:'disconnect'});
    offline?.close();
    emit('close');
    offline=offlineSession(emit,spawn);
    offline.open();
  };
  window.photonNetworkEvent=(event,token,data)=>{
    if(token!==session||offline)return;
    emit(event,data);
  };
  local.onclick=()=>{
    if(!offline)startOffline();
    document.querySelector('#open-create-room')?.click();
  };
  online.onclick=()=>{input.value=address;error.textContent='';dialog.showModal();};
  dialog.querySelector('[data-cancel]').onclick=()=>dialog.close();
  dialog.querySelector('form').onsubmit=e=>{
    e.preventDefault();
    try {
      const raw=input.value.trim();
      const url=new URL(raw.includes('://')?raw:'ws://'+raw);
      if(url.protocol==='https:')url.protocol='wss:';
      if(url.protocol==='http:')url.protocol='ws:';
      if(!['ws:','wss:'].includes(url.protocol)||!url.hostname||url.username||url.password)throw new Error();
      url.pathname='/room';url.search='';url.hash='';
      address=raw;try{localStorage.setItem('photon-room-address',address);}catch{}
      offline?.close();offline=null;
      emit('close');
      socket.readyState=0;
      nativeMessage({type:'connect',session:++session,address:url.href});
      dialog.close();
    }catch{error.textContent='请输入有效的局域网主机名或 IP 地址，可附端口。';}
  };
  socket.updateView=room=>{
    const items=document.querySelector('[data-mode="items"]');
    if(offline&&items){items.disabled=true;items.title='连接局域网房间服务后可使用个人道具模式';}
    else if(items)items.removeAttribute('title');
  };
  try{address=localStorage.getItem('photon-room-address')||'';}catch{}
  queueMicrotask(startOffline);
  return socket;
}
