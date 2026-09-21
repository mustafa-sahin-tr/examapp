import { Component, AfterViewInit, inject } from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import gsap from 'gsap';

@Component({
  selector: 'app-spin-wheel',
  standalone: true,
  templateUrl: './spin-wheel.component.html',
  styleUrls: ['./spin-wheel.component.scss'],
  imports: [TranslocoDirective],
})
export class SpinWheelComponent implements AfterViewInit {
  private readonly transloco = inject(TranslocoService);
  /** Çark üzerinde çizilen ödül adları — `shared.spinWheel.prizes.*` (issue #183). */
  prizes = ['dessert', 'tenPoints', 'extraQuestion', 'surprise', 'extraLife'].map(
    (key) => this.transloco.translate<string>(`shared.spinWheel.prizes.${key}`) ?? ''
  );
  wheelCanvas!: HTMLCanvasElement;
  wheelCtx!: CanvasRenderingContext2D;
  isSpinning = false; // Tekrar tıklamayı önlemek için
  finalRotation = 0; // Ka  zananı belirlemek için
  prize!: string;
  ngAfterViewInit() {
    this.wheelCanvas = document.getElementById('wheelCanvas') as HTMLCanvasElement;
    this.wheelCtx = this.wheelCanvas.getContext('2d')!;
    this.drawWheel();
    this.spinWheel();
  }

  // drawWheel() {
  //   const ctx = this.wheelCtx;
  //   const segments = this.prizes.length;
  //   const radius = this.wheelCanvas.width / 2;
  
  //   // 🎨 Ödüllere karşılık gelen renkler (Burayı istediğin gibi özelleştirebilirsin)
  //   const colors = ['#8B0000', '#FF4500', '#DAA520', '#008000', '#00008B']; 
  
  //   ctx.clearRect(0, 0, this.wheelCanvas.width, this.wheelCanvas.height);
  
  //   for (let i = 0; i < segments; i++) {
  //     ctx.beginPath();
  //     ctx.moveTo(radius, radius);
  
  //     // 🎯 Başlangıç açısını -90 dereceye ayarlayarak ilk ödülü tam yukarı hizalıyoruz
  //     const startAngle = ((i * 2 * Math.PI) / segments) - Math.PI / 2;
  //     const endAngle = (((i + 1) * 2 * Math.PI) / segments) - Math.PI / 2;
  
  //     ctx.arc(radius, radius, radius, startAngle, endAngle);
      
  //     // 🟢 Renkleri sırasıyla atıyoruz
  //     ctx.fillStyle = colors[i % colors.length]; // Renkleri sırayla döndür
  //     ctx.fill();
  //     ctx.stroke();
  
  //     // 📝 Ödül ismini ekle
  //     ctx.save();
  //     ctx.translate(radius, radius);
  //     ctx.rotate(startAngle + (Math.PI / segments)); // Yazıyı merkeze hizala
  //     ctx.fillStyle = 'white'; // Yazı rengini beyaz yaptık (daha okunaklı olsun diye)
  //     ctx.font = '16px Arial';
  //     ctx.fillText(this.prizes[i], radius / 2.5, 10);
  //     ctx.restore();
  //   }
  // }

  drawWheel() {
    const ctx = this.wheelCtx;
    const segments = this.prizes.length;
    const radius = this.wheelCanvas.width / 2;
  
    // 🎨 Daha koyu renkler
    const colors = ['#8B0000', '#FF4500', '#DAA520', '#008000', '#00008B']; 
  
    ctx.clearRect(0, 0, this.wheelCanvas.width, this.wheelCanvas.height);
  
    for (let i = 0; i < segments; i++) {
      ctx.beginPath();
      ctx.moveTo(radius, radius);
  
      const startAngle = ((i * 2 * Math.PI) / segments) - Math.PI / 2;
      const endAngle = (((i + 1) * 2 * Math.PI) / segments) - Math.PI / 2;
  
      ctx.arc(radius, radius, radius, startAngle, endAngle);
      ctx.fillStyle = colors[i % colors.length]; // 📌 Renkleri sırayla ata
      ctx.fill();
      ctx.stroke();
  
      // 📝 Yazıyı ekle
      ctx.save();
      ctx.translate(radius, radius);
      ctx.rotate(startAngle + (Math.PI / segments)); // Yazıyı merkeze hizala
      ctx.fillStyle = 'white'; // 📌 Daha okunaklı yapmak için beyaz kullan
      ctx.font = '16px Arial';
      ctx.fillText(this.prizes[i], radius / 2.5, 10);
      ctx.restore();
    }
  }
  
  

  spinWheel() {
    gsap.set(this.wheelCanvas, { rotation: 0 }); // Çarkın açısını sıfırla
    
    if (this.isSpinning) return; // Çark dönerken tekrar döndürmeyi engelle
    this.isSpinning = true;

    const totalSpins = 360 * 10; // 🔥 10 Tam Tur Atacak!
    const randomExtra = Math.floor(Math.random() * 360); // Ekstra rastgele dönüş açısı
    const randomRotation = totalSpins + randomExtra; // Toplam dönüş açısı

    gsap.to(this.wheelCanvas, {
      rotation: randomRotation,
      duration: 5, // 🔥 5 saniye dönsün
      ease: 'power4.out', // Yavaşça durma efekti
      onComplete: () => {
        this.calculateWinningPrize(randomRotation); // Kazananı hesapla
        // gsap.set(this.wheelCanvas, { rotation: 0 }); // Çarkın açısını sıfırla
        this.isSpinning = false;
      }
    });
  }


  calculateWinningPrize(finalRotation: number) {
    const segmentSize = 360 / this.prizes.length; // Ödüllerin açı genişliği
    const normalizedRotation = finalRotation % 360; // Gerçek kalan açı
    const adjustedRotation = (360 - normalizedRotation) % 360; // Başlangıç noktasının yeni konumu
    const winningIndex = Math.floor(adjustedRotation / segmentSize); // Kazanan ödülü bul
  
    alert(`🎉 Kazandığın ödül: ${this.prizes[winningIndex]}!`);
  }
  
  
}
