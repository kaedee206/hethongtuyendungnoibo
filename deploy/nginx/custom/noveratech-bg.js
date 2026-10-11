/**
 * NoveraTech ID - Cyber Meteors & Digital Space Background Engine
 * High-performance HTML5 Canvas animation adhering to docs/RULE.md.
 */

(function () {
  'use strict';

  // 1. Lock document title according to NoveraTech ID Brand guidelines
  try {
    Object.defineProperty(document, 'title', {
      get: function () {
        return 'NoveraTech ID · Cổng Định Danh & Xác Thực Nội Bộ';
      },
      set: function () {},
      configurable: true
    });
    document.title = 'NoveraTech ID · Cổng Định Danh & Xác Thực Nội Bộ';
  } catch (e) {}

  // 2. Initialize Canvas when DOM is ready
  function initCyberBackground() {
    if (document.getElementById('nt-cyber-canvas')) return;

    var canvas = document.createElement('canvas');
    canvas.id = 'nt-cyber-canvas';
    canvas.style.position = 'fixed';
    canvas.style.top = '0';
    canvas.style.left = '0';
    canvas.style.width = '100vw';
    canvas.style.height = '100vh';
    canvas.style.pointerEvents = 'none';
    canvas.style.zIndex = '0';
    canvas.style.opacity = '0.9';

    if (document.body) {
      document.body.prepend(canvas);
    } else {
      document.addEventListener('DOMContentLoaded', function () {
        document.body.prepend(canvas);
      });
    }

    var ctx = canvas.getContext('2d');
    if (!ctx) return;

    var width = 0;
    var height = 0;
    var dpr = Math.min(window.devicePixelRatio || 1, 2);

    function handleResize() {
      dpr = Math.min(window.devicePixelRatio || 1, 2);
      width = window.innerWidth;
      height = window.innerHeight;
      canvas.width = Math.floor(width * dpr);
      canvas.height = Math.floor(height * dpr);
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    }

    window.addEventListener('resize', handleResize);
    handleResize();

    // Trajectory angle: ~36 degrees (diagonal falling from top-left to bottom-right)
    var angle = (36 * Math.PI) / 180;
    var cosA = Math.cos(angle);
    var sinA = Math.sin(angle);

    // Color definitions (Emerald, Cyan, Electric Mint)
    var COLOR_PALETTES = [
      {
        head: '#FFFFFF',
        glow: '#10B981',
        stops: [
          [0, 'rgba(16, 185, 129, 0)'],
          [0.4, 'rgba(16, 185, 129, 0.15)'],
          [0.75, 'rgba(52, 211, 153, 0.5)'],
          [0.95, 'rgba(16, 185, 129, 0.95)'],
          [1, 'rgba(255, 255, 255, 1)']
        ]
      },
      {
        head: '#E0F2FE',
        glow: '#06B6D4',
        stops: [
          [0, 'rgba(6, 182, 212, 0)'],
          [0.45, 'rgba(6, 182, 212, 0.15)'],
          [0.8, 'rgba(56, 189, 248, 0.6)'],
          [0.96, 'rgba(6, 182, 212, 0.95)'],
          [1, 'rgba(255, 255, 255, 1)']
        ]
      },
      {
        head: '#ECFDF5',
        glow: '#34D399',
        stops: [
          [0, 'rgba(5, 150, 105, 0)'],
          [0.5, 'rgba(5, 150, 105, 0.2)'],
          [0.85, 'rgba(52, 211, 153, 0.7)'],
          [0.97, 'rgba(167, 243, 208, 0.95)'],
          [1, 'rgba(255, 255, 255, 1)']
        ]
      }
    ];

    // Cyber Meteor Class (Diagonal Laser Data Beam)
    function CyberMeteor(isInitial) {
      this.reset(isInitial);
    }

    CyberMeteor.prototype.reset = function (isInitial) {
      this.length = Math.random() * 160 + 120; // 120px to 280px streak
      this.speed = Math.random() * 7 + 6;     // 6px to 13px per frame
      this.width = Math.random() * 1.2 + 0.8;  // Thin, sharp laser line: 0.8px - 2.0px
      this.palette = COLOR_PALETTES[Math.floor(Math.random() * COLOR_PALETTES.length)];
      this.opacity = Math.random() * 0.45 + 0.5; // 0.5 to 0.95
      this.pulseGlow = Math.random() > 0.65;

      if (isInitial) {
        this.x = Math.random() * (width + 600) - 200;
        this.y = Math.random() * (height + 400) - 200;
      } else {
        // Respawn from top or upper-left
        if (Math.random() < 0.65) {
          this.x = Math.random() * (width + 300) - 100;
          this.y = -60 - Math.random() * 100;
        } else {
          this.x = -150 - Math.random() * 80;
          this.y = Math.random() * (height * 0.6);
        }
      }
    };

    CyberMeteor.prototype.update = function () {
      this.x += cosA * this.speed;
      this.y += sinA * this.speed;

      var tailX = this.x - cosA * this.length;
      var tailY = this.y - sinA * this.length;

      // Off-screen check
      if (tailX > width + 50 || tailY > height + 50) {
        this.reset(false);
      }
    };

    CyberMeteor.prototype.draw = function () {
      var tailX = this.x - cosA * this.length;
      var tailY = this.y - sinA * this.length;

      var grad = ctx.createLinearGradient(tailX, tailY, this.x, this.y);
      for (var i = 0; i < this.palette.stops.length; i++) {
        grad.addColorStop(this.palette.stops[i][0], this.palette.stops[i][1]);
      }

      ctx.save();
      ctx.globalAlpha = this.opacity;

      // 1. Draw glowing laser trail
      ctx.beginPath();
      ctx.moveTo(tailX, tailY);
      ctx.lineTo(this.x, this.y);
      ctx.strokeStyle = grad;
      ctx.lineWidth = this.width;
      ctx.lineCap = 'round';
      ctx.shadowColor = this.palette.glow;
      ctx.shadowBlur = this.pulseGlow ? 12 : 8;
      ctx.stroke();

      // 2. Draw high-energy head particle (Photon data packet)
      ctx.beginPath();
      ctx.arc(this.x, this.y, this.width * 1.15, 0, Math.PI * 2);
      ctx.fillStyle = this.palette.head;
      ctx.shadowColor = '#FFFFFF';
      ctx.shadowBlur = 10;
      ctx.fill();

      ctx.restore();
    };

    // Ambient Quantum Floating Nodes (Cyber Data Dust)
    function QuantumNode() {
      this.reset();
    }

    QuantumNode.prototype.reset = function () {
      this.x = Math.random() * width;
      this.y = Math.random() * height;
      this.radius = Math.random() * 1.2 + 0.6;
      this.alpha = Math.random() * 0.35 + 0.1;
      this.speedY = -(Math.random() * 0.2 + 0.05); // Gently floats upward
      this.speedX = (Math.random() - 0.5) * 0.15;
      this.color = Math.random() > 0.5 ? '16, 185, 129' : '6, 182, 212';
      this.blinkSpeed = Math.random() * 0.02 + 0.008;
      this.blinkFactor = Math.random() * Math.PI;
    };

    QuantumNode.prototype.update = function () {
      this.x += this.speedX;
      this.y += this.speedY;
      this.blinkFactor += this.blinkSpeed;

      if (this.y < -10) this.y = height + 10;
      if (this.x < -10) this.x = width + 10;
      if (this.x > width + 10) this.x = -10;
    };

    QuantumNode.prototype.draw = function () {
      var currentAlpha = this.alpha * (0.6 + 0.4 * Math.sin(this.blinkFactor));
      ctx.save();
      ctx.beginPath();
      ctx.arc(this.x, this.y, this.radius, 0, Math.PI * 2);
      ctx.fillStyle = 'rgba(' + this.color + ', ' + currentAlpha + ')';
      ctx.shadowColor = 'rgba(' + this.color + ', 0.6)';
      ctx.shadowBlur = 4;
      ctx.fill();
      ctx.restore();
    };

    // Instantiate elements (14 cyber meteors, 35 data nodes)
    var meteors = [];
    for (var m = 0; m < 14; m++) {
      meteors.push(new CyberMeteor(true));
    }

    var nodes = [];
    for (var n = 0; n < 32; n++) {
      nodes.push(new QuantumNode());
    }

    // Animation Loop with Visibility API optimization
    var animId = null;

    function renderLoop() {
      if (document.visibilityState === 'hidden') {
        animId = null;
        return;
      }

      ctx.clearRect(0, 0, width, height);

      // Render floating nodes
      for (var i = 0; i < nodes.length; i++) {
        nodes[i].update();
        nodes[i].draw();
      }

      // Render cyber meteors
      for (var j = 0; j < meteors.length; j++) {
        meteors[j].update();
        meteors[j].draw();
      }

      animId = requestAnimationFrame(renderLoop);
    }

    document.addEventListener('visibilitychange', function () {
      if (document.visibilityState === 'visible' && !animId) {
        animId = requestAnimationFrame(renderLoop);
      }
    });

    animId = requestAnimationFrame(renderLoop);
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initCyberBackground);
  } else {
    initCyberBackground();
  }
})();
