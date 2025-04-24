import { NgFor } from '@angular/common';
import { Component, signal } from '@angular/core';

export interface Option {
    label: string;
    value: string;
    selected?: boolean;
    icon?: string;
}
export interface ProgramStep {
    title: string;
    description: string;
    options: Option[];
}

@Component({
  selector: 'app-program-create',
  imports: [NgFor],
  templateUrl: './program-create.component.html',
  styleUrl: './program-create.component.scss'
})
export class ProgramCreateComponent {
    currentIndex = signal(0);

    get currentStep(): ProgramStep {
        return this.programSteps[this.currentIndex()];
    }

    userProgramSteps: number[] = [];

    programSteps = [
        {   
          id: 1,
          title: 'Süreli mi yoksa soru sayısı takipli bir çalışma mı planlamak istersin?',
          description: 'Süreli mi yoksa soru sayısı takipli bir çalışma mı planlamak istersin?',
          options: [
              { label: 'Süreli Çalışma', value: 'time', selected: false, icon: 'icons/question-mark.svg', nextStep: 2 },
              { label: 'Soru Sayısı Takipli Çalışma', value: 'question', selected: false, icon: 'icons/timer.svg', nextStep: 3 },
              { label: 'Her ikisi', value: 'both', selected: false, icon: 'icons/all.svg', nextStep: 4 },
            ]
        },
        { 
          id: 2,  
          title: 'Sana uygun olan çalışma süresini seçebilirsin.',
          description: 'Sana uygun olan çalışma süresini seçebilirsin.',
          options: [
             { label: '25 dakika çalışma 5 dakika ara', value: '25-5' , selected: false, icon: 'icons/question-mark.svg' ,  nextStep: 3 },
             { label: '30 dakika çalışma 10 dakika ara', value: '30-10' , selected: false, icon: 'icons/question-mark.svg' ,  nextStep: 3 },
             { label: '40 dakika çalışma 10 dakika ara', value: '40-10' , selected: false, icon: 'icons/question-mark.svg' ,  nextStep: 3 },
             { label: '50 dakika çalışma 10 dakika ara', value: '50-10' , selected: false, icon: 'icons/question-mark.svg' ,  nextStep: 3 },
            ]
        },
     {   
      id: 3,
      title: 'Bir dersten bir günde kaç soru çözersin?',
      description: 'Bir dersten bir günde kaç soru çözersin?',
      options: [
         { label: '8', value: '8',selected: false, icon: 'icons/question-mark.svg'  ,  nextStep: 4 },
         { label: '12', value: '12', selected: false, icon: 'icons/question-mark.svg' ,  nextStep: 4  },
         { label: '16', value: '16', selected: false, icon: 'icons/question-mark.svg'  ,  nextStep: 4 }
         ]
    },
    {  
      id: 4,
      title: 'Süreli mi yoksa soru sayısı takipli bir çalışma mı planlamak istersin',
      description: 'Süreli mi yoksa soru sayısı takipli bir çalışma mı planlamak istersin',
      options: [
        { label: 'Süreli Çalışma', value: 'time' , selected: false, icon: 'icons/question-mark.svg' , nextStep: -1  },
        { label: 'Soru Sayısı Takipli Çalışma', value: 'question', selected: false, icon: 'icons/question-mark.svg' ,nextStep: -1 }
        ]
    }
    ];

    next() {
        this.userProgramSteps.push(this.currentIndex());
        const nextStep = this.programSteps.findIndex(s => s.id === this.programSteps[this.currentIndex()].options.find(o => o.selected)?.nextStep);                      
        if (nextStep !== undefined && nextStep !== -1) {
            this.currentIndex.set(nextStep);
        } else {
            // Eğer bir sonraki adım yoksa, işlemi tamamla veya başka bir şey yap
            console.log('Program oluşturma işlemi tamamlandı.');
        }    
    }
    previous() {
        const previousStep = this.userProgramSteps.pop();
        if (previousStep !== undefined) {
            this.currentIndex.set(previousStep);

            console.log('Previous step:', this.programSteps[this.currentIndex()]);  
        }
    }

    selectOption(step: any, option: Option) {
        this.programSteps.forEach(s => {
            s.options.forEach(o => {
                o.selected = option.value === o.value;
            });
        }
      );
        
    }
}
