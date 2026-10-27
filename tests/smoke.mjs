import assert from 'node:assert/strict';
import {spawn, spawnSync} from 'node:child_process';
import {mkdirSync, mkdtempSync, writeFileSync, readFileSync, existsSync} from 'node:fs';
import {tmpdir, networkInterfaces} from 'node:os';
import path from 'node:path';
import http from 'node:http';
import net from 'node:net';
import {fileURLToPath} from 'node:url';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const exe = path.resolve(process.argv[2] || path.join(root, 'build', 'StreamCell.exe'));
const testRoot = path.join(root, 'build', 'tests'); mkdirSync(testRoot, {recursive:true});
const temp = mkdtempSync(path.join(testRoot, 'streamcell-test-'));
const port = await new Promise(resolve => {const s=net.createServer();s.listen(0,'127.0.0.1',()=>{const p=s.address().port;s.close(()=>resolve(p));});});
let log = '', checks = 0;
const server=spawn(exe,['--no-browser','--port',String(port),'--data-dir',temp],{windowsHide:true});
server.stdout.on('data',b=>{log+=b.toString('utf8');});
server.stderr.on('data',b=>{log+=b.toString('utf8');});
const delay=ms=>new Promise(r=>setTimeout(r,ms));
async function waitFor(fn) { for(let i=0;i<100;i++){if(fn())return;await delay(100);}throw Error('Timeout waiting for test server'); }
function call(route,{method='GET',body,headers={},hostname='127.0.0.1'}={}) {
  return new Promise((resolve,reject)=>{
    const text=body===undefined?null:JSON.stringify(body);
    const req=http.request({hostname,port,path:route,method,headers:{...(text===null?{}:{'Content-Type':'application/json','Content-Length':Buffer.byteLength(text)}),...headers}},res=>{let data='';res.on('data',b=>data+=b);res.on('end',()=>{let parsed;try{parsed=JSON.parse(data);}catch{parsed=data;}resolve({status:res.statusCode,body:parsed,raw:data});});});
    req.on('error',reject);if(text!==null)req.write(text);req.end();
  });
}
function check(value, message) {assert.ok(value,message);checks++;}
try {
  const infoPath=path.join(temp,'test-info.json');
  await waitFor(()=>existsSync(infoPath));
  const {adminToken,pairCode}=JSON.parse(readFileSync(infoPath,'utf8'));
  const admin={'X-StreamCell-Admin':adminToken};
  check((await call('/')).status===200,'Mobile page loads');
  check((await call('/admin',{headers:admin})).status===200,'Admin page loads');
  check((await call('/api/admin/config')).status===403,'Admin requires token');
  check((await call('/api/deck')).status===401,'Deck requires pairing');
  const credentials={username:'streamer',password:'local-test-password'};
  const account=await call('/api/admin/users/create',{method:'POST',body:credentials,headers:admin});
  check(account.status===200&&account.body.firstAdmin,'First local account becomes admin');
  const users=await call('/api/admin/users',{headers:admin});
  check(users.status===200&&users.body.users.length===1&&!users.raw.includes('passwordHash')&&!users.raw.includes('salt'),'User listing omits password hashes');
  check((await call('/api/pair',{method:'POST',body:{code:'WRONG'}})).status===401,'Invalid pairing blocked');
  check((await call('/api/pair',{method:'POST',body:{code:pairCode},headers:{Origin:'https://evil.example'}})).status===403,'Cross origin pairing blocked');
  check((await call('/api/pair',{method:'POST',body:{code:pairCode},headers:{Host:'evil.example:'+port}})).status===403,'DNS rebinding host blocked');
  const paired=await call('/api/pair',{method:'POST',body:{...credentials,code:pairCode}});
  const phone={'X-StreamCell-Token':paired.body.token};
  const second=await call('/api/pair',{method:'POST',body:{...credentials,code:pairCode}});
  check(paired.status===200 && second.status===200 && paired.body.token!==second.body.token,'Independent sessions');
  check((await call('/api/deck',{headers:phone})).status===200,'First phone still connected');
  check((await call('/api/admin/apps',{headers:phone})).status===403,'Phone cannot enumerate apps');
  const lan=Object.values(networkInterfaces()).flat().find(i=>i.family==='IPv4'&&!i.internal&&(/^(10\.|192\.168\.|172\.(1[6-9]|2\d|3[01])\.)/.test(i.address)));
  if(lan){
    check((await call('/api/admin/config',{hostname:lan.address,headers:admin})).status===403,'Even admin token cannot edit over LAN');
    check((await call('/admin',{hostname:lan.address})).status===403,'Admin page blocked over LAN');
    check((await call('/api/deck',{hostname:lan.address,headers:phone})).status===200,'Phone can read deck over LAN');
  }
  const stub=path.join(temp,'Fixture.exe'), marker=path.join(temp,'executed.txt'), cs=path.join(temp,'Fixture.cs');
  writeFileSync(cs,'class Fixture { static void Main(string[] args) { System.IO.File.WriteAllText(args[0], "executed"); } }');
  let csc=path.join(process.env.WINDIR,'Microsoft.NET','Framework64','v4.0.30319','csc.exe');
  if(!existsSync(csc)) csc=path.join(process.env.WINDIR,'Microsoft.NET','Framework','v4.0.30319','csc.exe');
  const compile=spawnSync(csc,['/nologo','/target:winexe','/out:'+stub,cs],{windowsHide:true});
  assert.equal(compile.status,0,'Fixture compilation');
  const config=(await call('/api/admin/config',{headers:admin})).body;
  config.title='Test deck';config.obs.password='fixture-secret-not-for-phones';config.obs.port=65534;
  config.pages[0].buttons=[{id:'fixture',slot:0,label:'Fixture',icon:'app',color:'#8576ff',type:'app',target:stub,arguments:'"'+marker+'"',soundIndex:1,keys:'',confirm:true}];
  const saved=await call('/api/admin/config',{headers:admin,method:'POST',body:config});
  check(saved.status===200 && saved.body.obs.passwordSet,'PC saves configuration and protected OBS password: '+saved.status+' '+(saved.body.error || '')+' '+(log.match(/Falha interna: (.+)/)?.[1] || ''));
  const disk=readFileSync(path.join(temp,'config.json'),'utf8');
  check(!disk.includes('fixture-secret-not-for-phones'),'OBS password encrypted at rest');
  const deck=await call('/api/deck',{headers:phone});
  check(deck.body.title==='Test deck' && deck.body.pages[0].buttons.length===1,'Phone reflects saved PC deck');
  check(!deck.raw.includes('Fixture.exe')&&!deck.raw.includes('arguments')&&!deck.raw.includes('password')&&!deck.raw.includes('fixture-secret'),'Phone payload excludes private parameters');
  check((await call('/api/admin/config',{headers:phone,method:'POST',body:config})).status===403,'Phone cannot save config');
  check((await call('/api/execute',{headers:phone,method:'POST',body:{id:'missing'}})).status===404,'Unknown action blocked');
  check((await call('/api/execute',{headers:phone,method:'POST',body:{id:'fixture'}})).status===409,'Confirmation required');
  check((await call('/api/execute',{headers:phone,method:'POST',body:{id:'fixture',confirmed:true,target:'other.exe'}})).status===400,'Phone cannot override target');
  check((await call('/api/execute',{headers:phone,method:'POST',body:{id:'fixture',confirmed:true}})).status===200,'Configured app executes');
  await waitFor(()=>existsSync(marker));check(readFileSync(marker,'utf8')==='executed','Execution reached only test fixture');
  const duplicate=structuredClone(config);duplicate.pages[0].buttons.push({...duplicate.pages[0].buttons[0],id:'duplicate'});
  check((await call('/api/admin/config',{headers:admin,method:'POST',body:duplicate})).status===400,'Duplicate positions rejected');
  config.pages[0].buttons=[];delete config.obs.password;
  check((await call('/api/admin/config',{headers:admin,method:'POST',body:config})).status===200,'PC can remove a button');
  await delay(200);
  check((await call('/api/execute',{headers:phone,method:'POST',body:{id:'fixture',confirmed:true}})).status===404,'Deleted button cannot run');
  await call('/api/admin/revoke',{headers:admin,method:'POST',body:{}});
  check((await call('/api/deck',{headers:phone})).status===401,'Revocation invalidates phone');
  check((await call('/api/deck',{headers:{'X-StreamCell-Token':second.body.token}})).status===401,'Revocation invalidates all sessions');
  console.log('PASS: '+checks+' integration checks. Temporary fixture only; no user apps or live broadcast were controlled.');
} finally {server.kill();}
