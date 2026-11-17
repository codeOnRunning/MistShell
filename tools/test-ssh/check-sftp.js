const { Client } = require('ssh2');
const c = new Client();
c.on('ready', () => {
  c.sftp((err, sftp) => {
    if (err) { console.error(err); process.exit(1); }
    sftp.readdir('/home/tester', (e, list) => {
      if (e) { console.error('READDIR', e); process.exit(1); }
      console.log('SFTP OK', list.map(x=>x.filename).join(','));
      sftp.readFile('/home/tester/welcome.txt', (e2, data) => {
        if (e2) { console.error('READ', e2); process.exit(1); }
        console.log('READ OK', data.toString().trim());
        c.end();
      });
    });
  });
});
c.on('error', e=>{console.error(e); process.exit(1);});
c.connect({host:'127.0.0.1',port:2222,username:'tester',password:'test123'});
