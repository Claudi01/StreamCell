const fs=require('node:fs'),path=require('node:path');
const project=path.resolve(__dirname,'..'), testDir=path.join(project,'build','tests','obs');
fs.mkdirSync(testDir,{recursive:true});
let compiler=path.join(process.env.WINDIR,'Microsoft.NET','Framework64','v4.0.30319','csc.exe');
if(!fs.existsSync(compiler)) compiler=path.join(process.env.WINDIR,'Microsoft.NET','Framework','v4.0.30319','csc.exe');
const testExe=path.join(testDir,'ObsHarness.exe');
const compilation=require('node:child_process').spawnSync(compiler,['/nologo','/target:exe','/out:'+testExe,'/reference:System.Web.Extensions.dll',path.join(project,'ObsBridge.cs'),path.join(__dirname,'ObsHarness.cs')],{windowsHide:true,stdio:'inherit'});
if(compilation.status!==0) process.exit(1);
const net=require("node:net"),crypto=require("node:crypto"),cp=require("node:child_process");
const sha=x=>crypto.createHash("sha256").update(x).digest("base64");
const expected=sha(sha("test-passwordsalt")+"challenge");
let scene="Main",muted=false,stream=false,record=false,connections=0;
const requests=[];
function frame(op,payload,fin=true) {
 const b=Buffer.from(payload); let h;
 if(b.length<126) h=Buffer.from([(fin?128:0)|op,b.length]);
 else {h=Buffer.alloc(4);h[0]=(fin?128:0)|op;h[1]=126;h.writeUInt16BE(b.length,2);}
 return Buffer.concat([h,b]);
}
const server=net.createServer(socket=>{
 connections++;let handshake=false,buffer=Buffer.alloc(0);
 const send=obj=> {
   const str=JSON.stringify(obj),split=Math.floor(str.length/2);
   socket.write(frame(1,str.slice(0,split),false));
   socket.write(frame(0,str.slice(split)));
 };
 socket.on("error",()=>{});
 socket.on("data",chunk=>{
  buffer=Buffer.concat([buffer,chunk]);
  if(!handshake) {
   const end=buffer.indexOf("\r\n\r\n");if(end<0)return;
   const headers=buffer.subarray(0,end).toString(),key=headers.match(/Sec-WebSocket-Key:\s*(.+)/i)[1].trim();
   const accept=crypto.createHash("sha1").update(key+"258EAFA5-E914-47DA-95CA-C5AB0DC85B11").digest("base64");
   socket.write("HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: "+accept+"\r\n\r\n");
   buffer=buffer.subarray(end+4);handshake=true;send({op:0,d:{rpcVersion:1,authentication:{salt:"salt",challenge:"challenge"}}});
  }
  while(buffer.length>=2) {
   const op=buffer[0]&15,mask=!!(buffer[1]&128);let n=buffer[1]&127,offset=2;
   if(n===126){if(buffer.length<4)return;n=buffer.readUInt16BE(2);offset=4;}
   if(n===127){socket.destroy();return;}
   if(buffer.length<offset+(mask?4:0)+n)return;
   const key=mask?buffer.subarray(offset,offset+4):null;if(mask)offset+=4;
   const payload=Buffer.from(buffer.subarray(offset,offset+n));buffer=buffer.subarray(offset+n);
   if(mask)for(let i=0;i<n;i++)payload[i]^=key[i%4];
   if(op===8){socket.end();return;}if(op===9){socket.write(frame(10,payload));continue;}if(op!==1)continue;
   const msg=JSON.parse(payload.toString());
   if(msg.op===1) {
    if(msg.d.authentication!==expected){const code=Buffer.alloc(2);code.writeUInt16BE(4009);socket.end(frame(8,code));return;}
    if(msg.d.rpcVersion!==1 || msg.d.eventSubscriptions!==0) throw Error("invalid identify");
    send({op:2,d:{negotiatedRpcVersion:1}});continue;
   }
   const d=msg.d,r=d.requestData||{};let data={},ok=true;requests.push(d.requestType);
   switch(d.requestType){
    case "GetSceneList":data={currentProgramSceneName:scene,scenes:[{sceneName:"Main"},{sceneName:"Game"}]};break;
    case "GetStreamStatus":data={outputActive:stream};break;
    case "GetRecordStatus":data={outputActive:record};break;
    case "GetInputList":data={inputs:[{inputName:"Mic"},{inputName:"SilentImage"}]};break;
    case "GetSpecialInputs":data={desktop1:"Desktop",desktop2:null,mic1:"Mic",mic2:null};break;
    case "GetInputMute":if(r.inputName==="SilentImage")ok=false;else data={inputMuted:r.inputName==="Mic"?muted:false};break;
    case "SetInputMute":muted=r.inputMuted;break;
    case "SetCurrentProgramScene":if(r.sceneName==="Timeout")continue;if(r.sceneName==="private-target")ok=false;else scene=r.sceneName;break;
    case "StartStream":if(stream)throw Error("bad stream toggle");stream=true;break;
    case "StopStream":if(!stream)throw Error("bad stream toggle");stream=false;break;
    case "StartRecord":if(record)throw Error("bad record toggle");record=true;break;
    case "StopRecord":if(!record)throw Error("bad record toggle");record=false;break;
    default:throw Error("unexpected request "+d.requestType);
   }
   send({op:5,d:{eventType:"Ignored"}});
   send({op:7,d:{requestType:d.requestType,requestId:d.requestId,requestStatus:{result:ok,code:ok?100:600,comment:"private-target"},responseData:data}});
  }
 });
});
server.listen(0,"127.0.0.1",()=>{
 const test=cp.spawn(testExe,[String(server.address().port)],{stdio:"inherit",windowsHide:true});
 test.on("exit",code=>{console.log("Connections:",connections,"Requests:",requests.length);server.close();process.exit(code);});
});