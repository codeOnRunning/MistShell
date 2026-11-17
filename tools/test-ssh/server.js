'use strict';

const fs = require('fs');
const path = require('path');
const os = require('os');
const { Server, utils: { sftp: { OPEN_MODE, STATUS_CODE } } } = require('ssh2');

const ROOT = path.join(__dirname, 'files');
const HOME = '/home/tester';
fs.mkdirSync(path.join(ROOT, 'projects'), { recursive: true });
fs.mkdirSync(path.join(ROOT, 'logs'), { recursive: true });
fs.writeFileSync(path.join(ROOT, 'welcome.txt'), 'Welcome to the MistShell test server.\n');
fs.writeFileSync(path.join(ROOT, 'projects', 'demo.txt'), 'hello from mistshell\n');
fs.writeFileSync(path.join(ROOT, 'logs', 'app.log'), '2026-11-16 INFO test server ready\n');

const hostKeyPath = path.join(__dirname, 'host.key');
if (!fs.existsSync(hostKeyPath)) {
  const { generateKeyPairSync } = require('crypto');
  const { privateKey } = generateKeyPairSync('rsa', {
    modulusLength: 2048,
    privateKeyEncoding: { type: 'pkcs1', format: 'pem' },
    publicKeyEncoding: { type: 'pkcs1', format: 'pem' }
  });
  fs.writeFileSync(hostKeyPath, privateKey, { mode: 0o600 });
}

function remotePath(remote) {
  let clean = String(remote || '.').replace(/\\/g, '/');
  if (clean === '.' || clean === '') return HOME;
  if (clean === '~') clean = HOME;
  if (!clean.startsWith('/')) clean = HOME + '/' + clean;
  if (clean === HOME || clean === HOME + '/' || clean === '/') return ROOT;
  if (!clean.startsWith(HOME + '/')) return clean;
  const relative = clean.slice(HOME.length).replace(/^\/+/, '');
  const local = path.resolve(ROOT, relative);
  if (!local.startsWith(ROOT + path.sep) && local !== ROOT) throw new Error('outside root');
  return local;
}

function localToRemote(local) {
  const rel = path.relative(ROOT, local).replace(/\\/g, '/');
  return rel ? HOME + '/' + rel : HOME;
}

function attrsFor(local) {
  const st = fs.statSync(local);
  return { mode: st.mode, uid: 1000, gid: 1000, size: st.size, atime: st.atimeMs, mtime: st.mtimeMs };
}

function longname(local, name) {
  const st = fs.statSync(local);
  const mode = st.mode;
  const type = st.isDirectory() ? 'd' : '-';
  const bits = [
    mode & 0o400 ? 'r' : '-', mode & 0o200 ? 'w' : '-', mode & 0o100 ? 'x' : '-',
    mode & 0o040 ? 'r' : '-', mode & 0o020 ? 'w' : '-', mode & 0o010 ? 'x' : '-',
    mode & 0o004 ? 'r' : '-', mode & 0o002 ? 'w' : '-', mode & 0o001 ? 'x' : '-'
  ].join('');
  const date = new Date(st.mtimeMs).toISOString().slice(0, 16).replace('T', ' ');
  return `${type}${bits} 1 tester tester ${st.size} ${date} ${name}`;
}

function handleShell(stream) {
  let buffer = '';
  let cwd = HOME;

  function prompt() {
    stream.write(`\x1b[32mtester@mistshell\x1b[0m:\x1b[34m${cwd}\x1b[0m$ `);
  }

  stream.write('MistShell SSH test server\r\nType "help" for available commands.\r\n');
  prompt();

  stream.on('data', (chunk) => {
    const text = chunk.toString('utf8');
    for (const ch of text) {
      if (ch === '\r' || ch === '\n') {
        stream.write('\r\n');
        runCommand(buffer.trim());
        buffer = '';
        prompt();
      } else if (ch === '\x7f' || ch === '\b') {
        if (buffer.length > 0) {
          buffer = buffer.slice(0, -1);
          stream.write('\b \b');
        }
      } else if (ch === '\x03') {
        buffer = '';
        stream.write('^C\r\n');
        prompt();
      } else if (ch >= ' ') {
        buffer += ch;
        stream.write(ch);
      }
    }
  });

  stream.on('window-change', () => {});
  stream.on('close', () => stream.end());

  function runCommand(line) {
    if (!line) return;
    if (line === 'exit' || line === 'logout') {
      stream.write('logout\r\n');
      stream.end();
      return;
    }
    if (line === 'clear') {
      stream.write('\x1b[2J\x1b[H');
      return;
    }
    if (line === 'help') {
      stream.write('可用命令: help, echo, whoami, hostname, pwd, ls, cd, cat, uname, uptime, free, df, ps, top, clear, exit\r\n');
      return;
    }
    if (line.startsWith('echo ')) {
      stream.write(line.slice(5) + '\r\n');
      return;
    }
    if (line === 'whoami') { stream.write('tester\r\n'); return; }
    if (line === 'hostname') { stream.write('mistshell-test\r\n'); return; }
    if (line === 'pwd') { stream.write(cwd + '\r\n'); return; }
    if (line === 'uname' || line.startsWith('uname ')) {
      stream.write('Linux mistshell-test 6.8.0-test #1 SMP x86_64 GNU/Linux\r\n');
      return;
    }
    if (line === 'uptime' || line.startsWith('uptime ')) {
      stream.write(' 17:20:01 up 12 days,  3:21,  1 user,  load average: 0.42, 0.35, 0.28\r\n');
      return;
    }
    if (line.startsWith('free')) {
      stream.write('              total        used        free      shared  buff/cache   available\r\n');
      stream.write('Mem:          15Gi       4.0Gi       2.1Gi       120Mi       9.6Gi       11Gi\r\n');
      stream.write('Swap:        2.0Gi          0B       2.0Gi\r\n');
      return;
    }
    if (line.startsWith('df')) {
      stream.write('Filesystem      Size  Used Avail Use% Mounted on\r\n');
      stream.write('/dev/sda1       100G   50G   50G  50% /\r\n');
      return;
    }
    if (line.startsWith('ps')) {
      stream.write('  PID COMMAND         %CPU %MEM\r\n');
      stream.write('  821 nginx           4.2  1.1\r\n  921 node            2.1  3.4\r\n  311 sshd            0.2  0.2\r\n');
      return;
    }
    if (line === 'top' || line.startsWith('top ')) {
      stream.write('top - 17:20:01 up 12 days, load average: 0.42, 0.35, 0.28\r\n');
      stream.write('Tasks: 128 total, 1 running, 127 sleeping\r\n');
      stream.write('%Cpu(s):  8.5 us,  2.1 sy, 89.4 id\r\n');
      return;
    }
    if (line === 'ls' || line.startsWith('ls ')) {
      const local = remotePath(cwd);
      try {
        const entries = fs.readdirSync(local, { withFileTypes: true });
        for (const e of entries) {
          const full = path.join(local, e.name);
          const st = fs.statSync(full);
          stream.write(`${st.isDirectory() ? 'd' : '-'} ${String(st.size).padStart(8)}  ${e.name}${e.isDirectory() ? '/' : ''}\r\n`);
        }
      } catch (e) {
        stream.write('ls: cannot access: ' + e.message + '\r\n');
      }
      return;
    }
    if (line.startsWith('cd ')) {
      const target = line.slice(3).trim();
      const next = target.startsWith('/') ? target : cwd + '/' + target;
      try {
        const local = remotePath(next);
        if (!fs.statSync(local).isDirectory()) throw new Error('not a directory');
        cwd = next.replace(/\/+/g, '/').replace(/\/$/, '') || '/';
      } catch {
        stream.write('cd: no such file or directory: ' + target + '\r\n');
      }
      return;
    }
    if (line.startsWith('cat ')) {
      const target = line.slice(4).trim();
      try {
        stream.write(fs.readFileSync(remotePath(cwd + '/' + target)).toString('utf8').replace(/\n/g, '\r\n'));
      } catch {
        stream.write('cat: no such file: ' + target + '\r\n');
      }
      return;
    }
    if (line.includes('@@UP')) {
      stream.write(
        '@@UP\r\n86400.00 12345.00\r\n' +
        '@@LOAD\r\n0.42 0.35 0.28 2/123 4567\r\n' +
        '@@CPU\r\ncpu  12000 100 3000 50000 1000 0 200 0 0 0\r\ncpu  12010 100 3010 50020 1000 0 200 0 0 0\r\n' +
        '@@MEM\r\nMemTotal: 16384000 kB\r\nMemFree: 4096000 kB\r\nMemAvailable: 8192000 kB\r\nBuffers: 200000 kB\r\nCached: 2000000 kB\r\nSwapTotal: 2097152 kB\r\nSwapFree: 2097152 kB\r\n' +
        '@@DISK\r\n/dev/sda1 104857600 52428800 52428800 50% /\r\n' +
        '@@NET\r\n  eth0: 123456789 1000 0 0 0 0 0 0 98765432 900 0 0 0 0 0 0\r\n' +
        '@@HOST\r\nmistshell-test\r\n' +
        '@@TOP\r\n  821 nginx 4.2 1.1\r\n  921 node 2.1 3.4\r\n  311 sshd 0.2 0.2\r\n'
      );
      return;
    }
    stream.write(`bash: ${line.split(' ')[0]}: command not found\r\n`);
  }
}

new Server({ hostKeys: [fs.readFileSync(hostKeyPath)] }, (client) => {
  console.log('client connected');
  client.on('error', (err) => console.error('client error:', err.message));
  client.on('authentication', (ctx) => {
    if (ctx.method === 'password' && ctx.username === 'tester' && ctx.password === 'test123') {
      ctx.accept();
    } else if (ctx.method === 'keyboard-interactive') {
      ctx.prompt([{ prompt: 'Password: ', echo: false }], (answers) => {
        if (answers && answers[0] === 'test123') ctx.accept(); else ctx.reject();
      });
    } else {
      ctx.reject(['password', 'keyboard-interactive']);
    }
  }).on('ready', () => {
    client.on('session', (accept) => {
      const session = accept();
      session.on('pty', (acceptPty) => acceptPty());
      session.on('shell', (acceptShell) => handleShell(acceptShell()));
      session.on('exec', (acceptExec, rejectExec, info) => {
        const stream = acceptExec();
        let output = '';
        if (info.command.includes('@@UP')) {
          output =
            '@@UP\r\n86400.00 12345.00\r\n' +
            '@@LOAD\r\n0.42 0.35 0.28 2/123 4567\r\n' +
            '@@CPU\r\ncpu  12000 100 3000 50000 1000 0 200 0 0 0\r\ncpu  12010 100 3010 50020 1000 0 200 0 0 0\r\n' +
            '@@MEM\r\nMemTotal: 16384000 kB\r\nMemFree: 4096000 kB\r\nMemAvailable: 8192000 kB\r\nBuffers: 200000 kB\r\nCached: 2000000 kB\r\nSwapTotal: 2097152 kB\r\nSwapFree: 2097152 kB\r\n' +
            '@@DISK\r\n/dev/sda1 104857600 52428800 52428800 50% /\r\n' +
            '@@NET\r\n  eth0: 123456789 1000 0 0 0 0 0 0 98765432 900 0 0 0 0 0 0\r\n' +
            '@@HOST\r\nmistshell-test\r\n' +
            '@@TOP\r\n  821 nginx 4.2 1.1\r\n  921 node 2.1 3.4\r\n  311 sshd 0.2 0.2\r\n';
        } else if (info.command.trim() !== 'true') {
          output = 'exec: ' + info.command + '\r\n';
        }
        stream.write(output);
        stream.exit(0);
        stream.end();
      });
      session.on('sftp', (acceptSftp) => {
        const sftp = acceptSftp();
        const handles = new Map();
        let nextHandle = 1;

        function makeHandle(kind, value) {
          const id = nextHandle++;
          handles.set(id, { kind, value });
          const buf = Buffer.alloc(4);
          buf.writeUInt32BE(id, 0);
          return buf;
        }
        function getHandle(buf) {
          if (!buf || buf.length !== 4) return null;
          return handles.get(buf.readUInt32BE(0)) || null;
        }
        const ok = (id) => sftp.status(id, STATUS_CODE.OK);
        const fail = (id) => sftp.status(id, STATUS_CODE.FAILURE);

        sftp.on('REALPATH', (reqid, p) => {
          let target;
          try { target = remotePath(p); } catch { target = ROOT; }
          const remote = typeof target === 'string' && target.startsWith('/') ? target : localToRemote(target);
          sftp.name(reqid, [{ filename: remote, longname: remote, attrs: {} }]);
        });
        sftp.on('OPENDIR', (reqid, p) => {
          try {
            const local = remotePath(p);
            const names = fs.readdirSync(local);
            sftp.handle(reqid, makeHandle('dir', { local, names, index: 0 }));
          } catch { fail(reqid); }
        });
        sftp.on('READDIR', (reqid, h) => {
          const entry = getHandle(h);
          if (!entry || entry.kind !== 'dir') return fail(reqid);
          if (entry.value.index >= entry.value.names.length) return sftp.status(reqid, STATUS_CODE.EOF);
          const name = entry.value.names[entry.value.index++];
          try {
            const local = path.join(entry.value.local, name);
            sftp.name(reqid, [{ filename: name, longname: longname(local, name), attrs: attrsFor(local) }]);
          } catch { fail(reqid); }
        });
        sftp.on('OPEN', (reqid, filename, flags) => {
          try {
            const local = remotePath(filename);
            let mode = '';
            if (flags & OPEN_MODE.READ) mode = (flags & OPEN_MODE.WRITE) ? 'r+' : 'r';
            else if (flags & OPEN_MODE.APPEND) mode = 'a';
            else mode = 'w';
            if (flags & OPEN_MODE.CREAT) fs.mkdirSync(path.dirname(local), { recursive: true });
            const fd = fs.openSync(local, mode);
            sftp.handle(reqid, makeHandle('file', { fd, local }));
          } catch (e) { console.error(e); fail(reqid); }
        });
        sftp.on('READ', (reqid, h, offset, length) => {
          const entry = getHandle(h);
          if (!entry || entry.kind !== 'file') return fail(reqid);
          try {
            const buffer = Buffer.alloc(length);
            const read = fs.readSync(entry.value.fd, buffer, 0, length, offset);
            if (read <= 0) sftp.status(reqid, STATUS_CODE.EOF);
            else sftp.data(reqid, buffer.subarray(0, read));
          } catch { fail(reqid); }
        });
        sftp.on('WRITE', (reqid, h, offset, data) => {
          const entry = getHandle(h);
          if (!entry || entry.kind !== 'file') return fail(reqid);
          try { fs.writeSync(entry.value.fd, data, 0, data.length, offset); ok(reqid); }
          catch { fail(reqid); }
        });
        sftp.on('CLOSE', (reqid, h) => {
          const entry = getHandle(h);
          if (!entry) return fail(reqid);
          try { if (entry.kind === 'file') fs.closeSync(entry.value.fd); } catch {}
          handles.delete(h.readUInt32BE(0));
          ok(reqid);
        });
        const onStat = (reqid, p) => {
          try { sftp.attrs(reqid, attrsFor(remotePath(p))); } catch { fail(reqid); }
        };
        const onFstat = (reqid, h) => {
          const entry = getHandle(h);
          if (!entry || entry.kind !== 'file') return fail(reqid);
          try { sftp.attrs(reqid, attrsFor(entry.value.local)); } catch { fail(reqid); }
        };
        sftp.on('STAT', onStat).on('LSTAT', onStat).on('FSTAT', onFstat);
        sftp.on('MKDIR', (reqid, p) => { try { fs.mkdirSync(remotePath(p)); ok(reqid); } catch { fail(reqid); } });
        sftp.on('RMDIR', (reqid, p) => { try { fs.rmdirSync(remotePath(p)); ok(reqid); } catch { fail(reqid); } });
        sftp.on('REMOVE', (reqid, p) => { try { fs.unlinkSync(remotePath(p)); ok(reqid); } catch { fail(reqid); } });
        sftp.on('RENAME', (reqid, from, to) => {
          try { fs.renameSync(remotePath(from), remotePath(to)); ok(reqid); } catch { fail(reqid); }
        });
        sftp.on('SETSTAT', (reqid, p, attrs) => {
          try {
            if (attrs.mode !== undefined) fs.chmodSync(remotePath(p), attrs.mode & 0o7777);
            ok(reqid);
          } catch { fail(reqid); }
        });
      });
    });
  }).on('close', () => console.log('client disconnected'));
}).listen(2222, '127.0.0.1', function () {
  console.log('MistShell test SSH listening on ' + this.address().address + ':' + this.address().port);
});
