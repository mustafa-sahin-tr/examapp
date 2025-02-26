import { Injectable } from '@angular/core';
import confetti from 'canvas-confetti';

@Injectable({
  providedIn: 'root'
})
export class ConfettiService {

  constructor() {}

  // 🎊 Simple Confetti: Küçük bir patlama
  celebrate() {
    confetti({
      particleCount: 100,
      spread: 70,
      origin: { y: 0.6 }
    });
  }

  // 💥 Complex Confetti: Büyük ve uzun süren patlama
  launchConfetti() {
    const duration = 3 * 1000; // 3 saniye sürecek
    const end = Date.now() + duration;

    (function frame() {
      confetti({
        particleCount: 5,
        spread: 160,
        startVelocity: 30,
        origin: { x: Math.random(), y: Math.random() - 0.2 }
      });

      if (Date.now() < end) {
        requestAnimationFrame(frame);
      }
    })();
  }

  // 🎆 Fireworks Confetti: Havai fişek tarzı patlama
  fireworks() {
    const defaults = {
      spread: 360,
      ticks: 50,
      gravity: 0,
      decay: 0.94,
      startVelocity: 30
    };

    confetti({
      ...defaults,
      particleCount: 50,
      scalar: 1.2
    });

    confetti({
      ...defaults,
      particleCount: 25,
      scalar: 0.75
    });
  }

  // 🌈 Rainbow Confetti: Gökkuşağı renklerinde konfeti
  rainbowConfetti() {
    confetti({
      particleCount: 200,
      spread: 120,
      colors: ['#ff0000', '#ff8000', '#ffff00', '#00ff00', '#0000ff', '#4b0082', '#9400d3']
    });
  }

  // 🎯 Center Confetti: Ekranın ortasında yoğun patlama
  centerBurst() {
    confetti({
      particleCount: 150,
      spread: 100,
      startVelocity: 40,
      origin: { x: 0.5, y: 0.5 }
    });
  }

  // 💨 Cannon Shot: Yandan yukarıya doğru ateşlenen konfeti
  cannonShot() {
    confetti({
      angle: 60,
      spread: 55,
      particleCount: 100,
      origin: { x: 0, y: 1 }
    });

    confetti({
      angle: 120,
      spread: 55,
      particleCount: 100,
      origin: { x: 1, y: 1 }
    });
  }
}
