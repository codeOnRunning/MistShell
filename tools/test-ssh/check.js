const { Client } = require('ssh2');
const c = new Client();
c.on('ready', () => {
  c.shell({ term:'xterm-256color', cols:100, rows:30 }, (err, stream) => {
    if (err) throw err;
    let out='';
    stream.on('data', d => { out += d.toString(); if(out.includes('$ ')) { console.log('SHELL OK:', JSON.stringify(out.slice(-120))); stream.write('whoami\r'); setTimeout(()=>{ console.log('AFTER:', JSON.stringify(out.slice(-100))); c.end(); }, 300); } });
    stream.on('close', ()=>c.end());
  });
});
c.on('error', e=>{console.error(e); process.exit(1);});
c.connect({host:'127.0.0.1',port:2222,username:'tester',password:'test123'});
