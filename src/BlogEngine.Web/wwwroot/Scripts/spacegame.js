// Gioco "Difesa Spaziale" (codice [gioco:difesa] nelle pagine): una formazione di droni geometrici scende
// avanzando di lato; la navetta in basso li abbatte prima che tocchino terra, riparandosi dietro barriere che si
// consumano. Ogni tanto passa una cometa: colpita vale punti extra e dà il colpo doppio per 10 secondi.
// Si gioca senza login; in classifica solo gli utenti collegati (classifica e chiamate in games.js).
// I colori arrivano dal tema con le variabili CSS --game-* sull'elemento .block-game.
(function () {
    'use strict';
    var G = window.BlogGames;
    if (!G) return;
    var el = G.el;

    // Campo logico (il disegno viene scalato alla larghezza disponibile).
    var W = 480, H = 420;
    var ROWS = 5, COLS = 9;
    var PLAYER_Y = H - 34;
    var GROUND = PLAYER_Y - 14;
    var POINTS = [30, 20, 20, 10, 10];
    var COMET_POINTS = [100, 200, 300];

    function Game(root) {
        this.root = root;
        this.labels = G.labels(root);
        this.state = 'idle';
        this.keys = {};
        this.stars = [];
        for (var i = 0; i < 60; i++) this.stars.push([Math.random() * W, Math.random() * H, Math.random() < 0.2 ? 1.6 : 1]);
        this.build();
        this.board.load();
    }

    Game.prototype.t = function (key) { return (this.labels && this.labels[key]) || key; };

    Game.prototype.build = function () {
        var self = this, root = this.root;
        root.innerHTML = '';
        root.setAttribute('tabindex', '0');

        var top = el('div', 'space-game-top');
        var stats = el('dl', 'block-game-stats space-game-stats');
        this.scoreValue = this.stat(stats, 'gameScore');
        this.waveValue = this.stat(stats, 'gameWave');
        this.livesValue = this.stat(stats, 'gameLives');
        top.appendChild(stats);
        this.pauseButton = el('button', 'block-game-pause btn', this.t('gamePause'));
        this.pauseButton.type = 'button';
        this.pauseButton.disabled = true;
        this.pauseButton.addEventListener('click', function () { self.togglePause(); });
        top.appendChild(this.pauseButton);
        root.appendChild(top);

        var stage = el('div', 'block-game-stage space-game-stage');
        this.canvas = el('canvas', 'block-game-board');
        this.canvas.setAttribute('role', 'img');
        this.canvas.setAttribute('aria-label', this.t('gameName'));
        stage.appendChild(this.canvas);
        this.overlay = el('div', 'block-game-overlay');
        this.overlayTitle = el('p', 'block-game-overlay-title');
        this.overlayText = el('p', 'block-game-overlay-text');
        this.startButton = el('button', 'block-game-start btn');
        this.startButton.type = 'button';
        this.startButton.addEventListener('click', function () { self.overlayAction(); });
        this.overlay.appendChild(this.overlayTitle);
        this.overlay.appendChild(this.overlayText);
        this.overlay.appendChild(this.startButton);
        stage.appendChild(this.overlay);
        root.appendChild(stage);

        // Pulsanti per il telefono: tenendoli premuti la navetta si muove o continua a sparare.
        var pad = el('div', 'block-game-pad');
        [['left', '◀', 'gameLeft'], ['fire', '●', 'gameFire'], ['right', '▶', 'gameRight']].forEach(function (b) {
            var button = el('button', 'block-game-key block-game-key-' + b[0], b[1]);
            button.type = 'button';
            button.title = self.t(b[2]);
            button.setAttribute('aria-label', self.t(b[2]));
            var press = function (on) { return function (e) { e.preventDefault(); self.keys[b[0]] = on; }; };
            button.addEventListener('pointerdown', press(true));
            ['pointerup', 'pointerleave', 'pointercancel'].forEach(function (ev) { button.addEventListener(ev, press(false)); });
            pad.appendChild(button);
        });
        root.appendChild(pad);

        var help = el('p', 'block-game-help', this.t('gameHelp'));
        if (window.matchMedia && window.matchMedia('(pointer: coarse)').matches) help.textContent = this.t('gameHelpTouch');
        root.appendChild(help);

        this.board = new G.Leaderboard(root, function (k) { return self.t(k); });

        var map = { ArrowLeft: 'left', ArrowRight: 'right', ' ': 'fire', Spacebar: 'fire', ArrowUp: 'fire' };
        root.addEventListener('keydown', function (e) {
            if (e.key === 'p' || e.key === 'P' || e.key === 'Escape') {
                if (self.state === 'playing' || self.state === 'paused') { e.preventDefault(); self.togglePause(); }
                return;
            }
            if (e.key === 'Enter' && (self.state === 'idle' || self.state === 'over')) { e.preventDefault(); self.overlayAction(); return; }
            if (map[e.key] && self.state === 'playing') { e.preventDefault(); self.keys[map[e.key]] = true; }
        });
        root.addEventListener('keyup', function (e) { if (map[e.key]) self.keys[map[e.key]] = false; });
        root.addEventListener('blur', function () { self.keys = {}; });
        document.addEventListener('visibilitychange', function () { if (document.hidden && self.state === 'playing') self.togglePause(); });
        window.addEventListener('resize', function () { self.resize(); self.draw(); });

        this.reset();
        this.resize();
        this.draw();
        this.showOverlay(this.t('gameName'), '', this.t('gameStart'));
    };

    Game.prototype.stat = function (list, key) {
        var box = el('div', 'space-game-stat');
        box.appendChild(el('dt', null, this.t(key)));
        var dd = el('dd', null, '0');
        box.appendChild(dd);
        list.appendChild(box);
        return dd;
    };

    Game.prototype.colors = function () {
        var style = getComputedStyle(this.root);
        var get = function (name, fallback) { var v = style.getPropertyValue(name).trim(); return v || fallback; };
        return {
            board: get('--game-board', '#0d1220'),
            star: get('--game-star', 'rgba(255,255,255,0.5)'),
            player: get('--game-accent', '#f0b44c'),
            shot: get('--game-c6', '#ffffff'),
            enemy: [get('--game-c5', '#a78bdb'), get('--game-c1', '#4fb3d9'), get('--game-c1', '#4fb3d9'), get('--game-c3', '#7cc576'), get('--game-c3', '#7cc576')],
            enemyShot: get('--game-c4', '#e46d6d'),
            barrier: get('--game-c2', '#f0b44c'),
            comet: get('--game-bomb', '#ff6b2c')
        };
    };

    Game.prototype.resize = function () {
        var available = this.root.clientWidth || 480;
        var maxHeight = Math.max(240, (window.innerHeight || 800) - 190);
        var width = Math.min(available, 560, maxHeight * W / H);
        this.scale = width / W;
        var ratio = window.devicePixelRatio || 1;
        this.canvas.width = Math.round(W * this.scale * ratio);
        this.canvas.height = Math.round(H * this.scale * ratio);
        this.canvas.style.width = Math.round(W * this.scale) + 'px';
        this.canvas.style.height = Math.round(H * this.scale) + 'px';
        // Il riquadro sopra il campo (inizio, pausa, fine) è largo quanto il campo.
        this.root.style.setProperty('--space-width', Math.round(W * this.scale) + 'px');
        this.canvas.getContext('2d').setTransform(this.scale * ratio, 0, 0, this.scale * ratio, 0, 0);
        this.palette = this.colors();
    };

    Game.prototype.reset = function () {
        this.score = 0;
        this.wave = 1;
        this.lives = 3;
        this.player = { x: W / 2, cooldown: 0, invincible: 0, double: 0 };
        this.shots = [];
        this.enemyShots = [];
        this.particles = [];
        this.comet = null;
        this.cometTimer = 15 + Math.random() * 8;
        this.buildBarriers();
        this.buildWave();
        this.updateStats();
    };

    Game.prototype.buildWave = function () {
        this.enemies = [];
        var startY = 52 + Math.min(this.wave - 1, 5) * 12;
        for (var r = 0; r < ROWS; r++) {
            for (var c = 0; c < COLS; c++) {
                this.enemies.push({ row: r, col: c, alive: true, x: 0, y: 0 });
            }
        }
        this.formation = { x: 40, y: startY, dir: 1 };
        this.enemyFireTimer = 1.2;
        this.placeEnemies();
    };

    Game.prototype.buildBarriers = function () {
        this.barriers = [];
        for (var b = 0; b < 4; b++) {
            var bx = 60 + b * 110, by = GROUND - 54;
            for (var y = 0; y < 4; y++) {
                for (var x = 0; x < 7; x++) {
                    if (y === 3 && (x === 2 || x === 3 || x === 4)) continue;
                    if (y === 0 && (x === 0 || x === 6)) continue;
                    this.barriers.push({ x: bx + x * 8, y: by + y * 8, hp: 3 });
                }
            }
        }
    };

    Game.prototype.placeEnemies = function () {
        var f = this.formation;
        this.enemies.forEach(function (e) {
            e.x = f.x + e.col * 40;
            e.y = f.y + e.row * 30;
        });
    };

    Game.prototype.alive = function () { return this.enemies.filter(function (e) { return e.alive; }); };

    Game.prototype.update = function (dt) {
        var self = this, p = this.player;

        // Navetta.
        var speed = 230;
        if (this.keys.left) p.x -= speed * dt;
        if (this.keys.right) p.x += speed * dt;
        p.x = Math.max(20, Math.min(W - 20, p.x));
        p.cooldown -= dt;
        p.invincible = Math.max(0, p.invincible - dt);
        p.double = Math.max(0, p.double - dt);
        if (this.keys.fire && p.cooldown <= 0 && this.shots.length < (p.double > 0 ? 4 : 2)) {
            if (p.double > 0) {
                this.shots.push({ x: p.x - 9, y: PLAYER_Y - 12 }, { x: p.x + 9, y: PLAYER_Y - 12 });
            } else {
                this.shots.push({ x: p.x, y: PLAYER_Y - 14 });
            }
            p.cooldown = 0.3;
        }

        // Colpi della navetta.
        this.shots.forEach(function (s) { s.y -= 430 * dt; });
        this.shots = this.shots.filter(function (s) { return s.y > -10 && !s.hit; });

        // Formazione: di lato, e giù di un passo quando tocca il bordo. Più veloce con meno droni e ondate alte.
        var alive = this.alive();
        var f = this.formation;
        var fSpeed = 26 * (1 + (this.wave - 1) * 0.15) * (1 + 2.2 * (1 - alive.length / (ROWS * COLS)));
        f.x += f.dir * fSpeed * dt;
        var minX = Math.min.apply(null, alive.map(function (e) { return e.col; })) * 40 + f.x;
        var maxX = Math.max.apply(null, alive.map(function (e) { return e.col; })) * 40 + f.x + 26;
        if ((f.dir > 0 && maxX >= W - 8) || (f.dir < 0 && minX <= 8)) {
            f.dir = -f.dir;
            f.y += 14;
        }
        this.placeEnemies();

        // Tiri dei droni: sempre dal più basso di una colonna a caso.
        this.enemyFireTimer -= dt;
        if (this.enemyFireTimer <= 0 && alive.length) {
            var cols = {};
            alive.forEach(function (e) { if (!cols[e.col] || cols[e.col].row < e.row) cols[e.col] = e; });
            var shooters = Object.keys(cols).map(function (k) { return cols[k]; });
            var shooter = shooters[Math.floor(Math.random() * shooters.length)];
            this.enemyShots.push({ x: shooter.x + 13, y: shooter.y + 18, vy: 150 + 18 * this.wave });
            this.enemyFireTimer = (0.5 + Math.random() * 0.9) / (1 + 0.12 * (this.wave - 1));
        }
        this.enemyShots.forEach(function (s) { s.y += s.vy * dt; });

        // Cometa: passa in alto ogni 15-25 secondi.
        this.cometTimer -= dt;
        if (!this.comet && this.cometTimer <= 0) {
            var fromLeft = Math.random() < 0.5;
            this.comet = { x: fromLeft ? -30 : W + 30, y: 26, vx: fromLeft ? 95 : -95 };
        }
        if (this.comet) {
            this.comet.x += this.comet.vx * dt;
            if (this.comet.x < -40 || this.comet.x > W + 40) { this.comet = null; this.cometTimer = 15 + Math.random() * 10; }
        }

        // Colpi contro droni, cometa e barriere.
        this.shots.forEach(function (s) {
            for (var i = 0; i < alive.length && !s.hit; i++) {
                var e = alive[i];
                if (e.alive && s.x >= e.x && s.x <= e.x + 26 && s.y >= e.y && s.y <= e.y + 20) {
                    e.alive = false;
                    s.hit = true;
                    self.score += POINTS[e.row];
                    self.explode(e.x + 13, e.y + 10, self.palette.enemy[e.row]);
                }
            }
            if (!s.hit && self.comet && Math.abs(s.x - self.comet.x) < 18 && Math.abs(s.y - self.comet.y) < 12) {
                s.hit = true;
                var bonus = COMET_POINTS[Math.floor(Math.random() * COMET_POINTS.length)];
                self.score += bonus;
                self.player.double = 10;
                self.explode(self.comet.x, self.comet.y, self.palette.comet);
                self.floating = { text: '+' + bonus, x: self.comet.x, y: self.comet.y, life: 1 };
                self.comet = null;
                self.cometTimer = 15 + Math.random() * 10;
            }
            if (!s.hit) self.hitBarrier(s);
        });
        this.shots = this.shots.filter(function (s) { return !s.hit; });

        // Tiri dei droni contro barriere e navetta.
        this.enemyShots.forEach(function (s) {
            if (self.hitBarrier(s)) return;
            if (!s.hit && p.invincible <= 0 && s.y >= PLAYER_Y - 10 && s.y <= PLAYER_Y + 10 && Math.abs(s.x - p.x) < 17) {
                s.hit = true;
                self.loseLife();
            }
        });
        this.enemyShots = this.enemyShots.filter(function (s) { return !s.hit && s.y < H; });

        // I droni che scendono consumano le barriere; se toccano terra la partita finisce.
        alive = this.alive();
        alive.forEach(function (e) {
            self.barriers.forEach(function (b) {
                if (b.hp > 0 && b.x + 8 > e.x && b.x < e.x + 26 && b.y + 8 > e.y && b.y < e.y + 20) b.hp = 0;
            });
        });
        if (alive.some(function (e) { return e.y + 20 >= GROUND; })) {
            this.lives = 0;
            this.gameOver();
            return;
        }

        // Ondata completata.
        if (!alive.length) {
            this.score += 500;
            this.wave++;
            this.floating = { text: this.t('gameWave') + ' ' + this.wave, x: W / 2, y: H / 2, life: 1.4 };
            this.enemyShots = [];
            this.buildWave();
        }

        this.particles.forEach(function (q) { q.x += q.vx * dt; q.y += q.vy * dt; q.life -= dt; });
        this.particles = this.particles.filter(function (q) { return q.life > 0; });
        if (this.floating) { this.floating.life -= dt; this.floating.y -= 20 * dt; if (this.floating.life <= 0) this.floating = null; }
        this.updateStats();
    };

    Game.prototype.hitBarrier = function (s) {
        for (var i = 0; i < this.barriers.length; i++) {
            var b = this.barriers[i];
            if (b.hp > 0 && s.x >= b.x && s.x <= b.x + 8 && s.y >= b.y && s.y <= b.y + 8) {
                b.hp--;
                s.hit = true;
                return true;
            }
        }
        return false;
    };

    Game.prototype.loseLife = function () {
        this.lives--;
        this.explode(this.player.x, PLAYER_Y, this.palette.player);
        this.enemyShots = [];
        if (this.lives <= 0) {
            this.gameOver();
            return;
        }
        this.player.invincible = 1.6;
    };

    Game.prototype.explode = function (x, y, color) {
        for (var i = 0; i < 10; i++) {
            var a = Math.random() * Math.PI * 2, v = 40 + Math.random() * 90;
            this.particles.push({ x: x, y: y, vx: Math.cos(a) * v, vy: Math.sin(a) * v, life: 0.35 + Math.random() * 0.3, color: color });
        }
    };

    Game.prototype.loop = function (now) {
        if (this.state !== 'playing') return;
        var dt = Math.min(0.05, (now - this.last) / 1000);
        this.last = now;
        this.update(dt);
        this.draw();
        if (this.state !== 'playing') return;
        var self = this;
        this.frame = requestAnimationFrame(function (t) { self.loop(t); });
    };

    Game.prototype.overlayAction = function () {
        if (this.state === 'paused') this.togglePause();
        else this.start();
    };

    Game.prototype.start = function () {
        var self = this;
        this.reset();
        this.keys = {};
        this.token = null;
        this.board.hideMessage();
        this.board.start().then(function (token) { self.token = token; });
        this.state = 'playing';
        this.overlay.hidden = true;
        this.pauseButton.disabled = false;
        this.pauseButton.textContent = this.t('gamePause');
        this.root.focus({ preventScroll: true });
        this.last = performance.now();
        this.frame = requestAnimationFrame(function (t) { self.loop(t); });
    };

    Game.prototype.togglePause = function () {
        var self = this;
        if (this.state === 'playing') {
            this.state = 'paused';
            this.keys = {};
            cancelAnimationFrame(this.frame);
            this.pauseButton.textContent = this.t('gameResume');
            this.showOverlay(this.t('gamePause'), '', this.t('gameResume'));
        } else if (this.state === 'paused') {
            this.state = 'playing';
            this.pauseButton.textContent = this.t('gamePause');
            this.overlay.hidden = true;
            this.root.focus({ preventScroll: true });
            this.last = performance.now();
            this.frame = requestAnimationFrame(function (t) { self.loop(t); });
        }
    };

    Game.prototype.gameOver = function () {
        if (this.state === 'over') return;
        this.state = 'over';
        this.keys = {};
        cancelAnimationFrame(this.frame);
        this.pauseButton.disabled = true;
        this.updateStats();
        this.draw();
        this.showOverlay(this.t('gameOver'), this.t('gameScore') + ': ' + this.score.toLocaleString(), this.t('gamePlayAgain'));
        // In classifica vanno le ondate superate.
        this.board.submit(this.token, this.score, this.wave - 1);
    };

    Game.prototype.showOverlay = function (title, body, button) {
        this.overlayTitle.textContent = title;
        this.overlayText.textContent = body;
        this.overlayText.hidden = !body;
        this.startButton.textContent = button;
        this.overlay.hidden = false;
    };

    Game.prototype.updateStats = function () {
        this.scoreValue.textContent = this.score.toLocaleString();
        this.waveValue.textContent = this.wave;
        this.livesValue.textContent = Math.max(0, this.lives);
    };

    // Droni: tre forme geometriche diverse secondo la fila.
    Game.prototype.drawEnemy = function (ctx, e, t) {
        var cx = e.x + 13, cy = e.y + 10, pulse = Math.sin(t / 220 + e.col) * 1.5;
        ctx.fillStyle = this.palette.enemy[e.row];
        ctx.beginPath();
        if (e.row === 0) {
            ctx.moveTo(cx, cy - 10); ctx.lineTo(cx + 12, cy); ctx.lineTo(cx, cy + 10); ctx.lineTo(cx - 12, cy);
        } else if (e.row < 3) {
            for (var i = 0; i < 6; i++) {
                var a = Math.PI / 3 * i + Math.PI / 6;
                ctx[i ? 'lineTo' : 'moveTo'](cx + Math.cos(a) * 11, cy + Math.sin(a) * 9);
            }
        } else {
            ctx.ellipse(cx, cy, 12, 8, 0, 0, Math.PI * 2);
        }
        ctx.closePath();
        ctx.fill();
        ctx.fillStyle = 'rgba(0,0,0,0.55)';
        ctx.fillRect(cx - 6, cy - 2, 4, 4);
        ctx.fillRect(cx + 2, cy - 2, 4, 4);
        ctx.fillStyle = this.palette.enemy[e.row];
        ctx.fillRect(cx - 11, cy + 8 + pulse * 0.5, 3, 3);
        ctx.fillRect(cx + 8, cy + 8 - pulse * 0.5, 3, 3);
    };

    Game.prototype.draw = function () {
        var ctx = this.canvas.getContext('2d'), pal = this.palette, self = this, t = performance.now();
        if (!pal) return;
        ctx.fillStyle = pal.board;
        ctx.fillRect(0, 0, W, H);
        ctx.fillStyle = pal.star;
        this.stars.forEach(function (s) { ctx.fillRect(s[0], s[1], s[2], s[2]); });

        // Terra.
        ctx.fillStyle = pal.barrier;
        ctx.globalAlpha = 0.35;
        ctx.fillRect(0, GROUND + 26, W, 2);
        ctx.globalAlpha = 1;

        this.barriers.forEach(function (b) {
            if (b.hp <= 0) return;
            ctx.globalAlpha = 0.35 + b.hp * 0.22;
            ctx.fillStyle = pal.barrier;
            ctx.fillRect(b.x, b.y, 8, 8);
        });
        ctx.globalAlpha = 1;

        this.enemies.forEach(function (e) { if (e.alive) self.drawEnemy(ctx, e, t); });

        if (this.comet) {
            var c = this.comet, tail = c.vx > 0 ? -1 : 1;
            var grad = ctx.createLinearGradient(c.x, c.y, c.x + tail * 46, c.y);
            grad.addColorStop(0, pal.comet);
            grad.addColorStop(1, 'rgba(0,0,0,0)');
            ctx.fillStyle = grad;
            ctx.beginPath();
            ctx.moveTo(c.x, c.y - 7); ctx.lineTo(c.x + tail * 46, c.y); ctx.lineTo(c.x, c.y + 7);
            ctx.fill();
            ctx.fillStyle = pal.comet;
            ctx.beginPath();
            ctx.arc(c.x, c.y, 8, 0, Math.PI * 2);
            ctx.fill();
        }

        // Navetta (lampeggia quando è appena stata colpita; alone quando ha il colpo doppio).
        var p = this.player;
        if (this.state !== 'over' && !(p.invincible > 0 && Math.floor(t / 100) % 2)) {
            if (p.double > 0) {
                ctx.fillStyle = pal.comet;
                ctx.globalAlpha = 0.25;
                ctx.beginPath();
                ctx.arc(p.x, PLAYER_Y, 22, 0, Math.PI * 2);
                ctx.fill();
                ctx.globalAlpha = 1;
            }
            ctx.fillStyle = pal.player;
            ctx.beginPath();
            ctx.moveTo(p.x, PLAYER_Y - 14);
            ctx.lineTo(p.x + 17, PLAYER_Y + 9);
            ctx.lineTo(p.x + 6, PLAYER_Y + 5);
            ctx.lineTo(p.x - 6, PLAYER_Y + 5);
            ctx.lineTo(p.x - 17, PLAYER_Y + 9);
            ctx.closePath();
            ctx.fill();
        }

        ctx.fillStyle = pal.shot;
        this.shots.forEach(function (s) { ctx.fillRect(s.x - 1.5, s.y - 6, 3, 10); });
        ctx.fillStyle = pal.enemyShot;
        this.enemyShots.forEach(function (s) {
            ctx.beginPath();
            ctx.moveTo(s.x, s.y + 6); ctx.lineTo(s.x + 3, s.y - 3); ctx.lineTo(s.x - 3, s.y - 3);
            ctx.fill();
        });

        this.particles.forEach(function (q) {
            ctx.globalAlpha = Math.max(0, q.life * 2);
            ctx.fillStyle = q.color;
            ctx.fillRect(q.x - 1.5, q.y - 1.5, 3, 3);
        });
        ctx.globalAlpha = 1;

        if (this.floating) {
            ctx.fillStyle = '#ffffff';
            ctx.globalAlpha = Math.min(1, this.floating.life * 2);
            ctx.font = 'bold 16px sans-serif';
            ctx.textAlign = 'center';
            ctx.fillText(this.floating.text, this.floating.x, this.floating.y);
            ctx.globalAlpha = 1;
        }
    };

    G.register('difesa', Game);
})();
