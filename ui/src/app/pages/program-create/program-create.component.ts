import { NgFor } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';

export interface Option {
  label: string;
  value: string;
  selected?: boolean;
  icon?: string;
}

export interface StepAction {
  label: string;
  value: string;
}

export interface ProgramStep {
  title: string;
  description: string;
  options: Option[];
  multiple: boolean;
  actions: StepAction[];
}

@Component({
  selector: 'app-program-create',
  imports: [NgFor, MatIconModule, MatButtonModule, MatSnackBarModule],
  templateUrl: './program-create.component.html',
  styleUrl: './program-create.component.scss',
})
export class ProgramCreateComponent {
  currentIndex = signal(0);
  stepList = [0, 1, 2, 3, 4, 5];
  stepIndex = signal(0);
  private snackBar = inject(MatSnackBar);
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
        {
          label: 'Soru Sayısı Takipli Çalışma',
          value: 'question',
          selected: false,
          icon: 'icons/timer.svg',
          nextStep: 3,
        },
      ],
      multiple: false,
      actions: [],
    },
    {
      id: 2,
      multiple: false,
      title: 'Sana uygun olan çalışma süresini seçebilirsin.',
      description: 'Sana uygun olan çalışma süresini seçebilirsin.',
      options: [
        {
          label: '25 dakika çalışma 5 dakika ara',
          value: '25-5',
          selected: false,
          icon: 'icons/question-mark.svg',
          nextStep: 5,
        },
        {
          label: '30 dakika çalışma 10 dakika ara',
          value: '30-10',
          selected: false,
          icon: 'icons/question-mark.svg',
          nextStep: 5,
        },
        {
          label: '40 dakika çalışma 10 dakika ara',
          value: '40-10',
          selected: false,
          icon: 'icons/question-mark.svg',
          nextStep: 5,
        },
        {
          label: '50 dakika çalışma 10 dakika ara',
          value: '50-10',
          selected: false,
          icon: 'icons/question-mark.svg',
          nextStep: 5,
        },
      ],
      actions: [],
    },
    {
      id: 3,
      title: 'Bir dersten bir günde kaç soru çözersin?',
      description: 'Bir dersten bir günde kaç soru çözersin?',
      multiple: false,
      options: [
        { label: '8', value: '8', selected: false, icon: 'icons/question-mark.svg', nextStep: 5 },
        { label: '12', value: '12', selected: false, icon: 'icons/question-mark.svg', nextStep: 5 },
        { label: '16', value: '16', selected: false, icon: 'icons/question-mark.svg', nextStep: 5 },
      ],
      actions: [],
    },
    {
      id: 4,
      title: 'Süreli mi yoksa soru sayısı takipli bir çalışma mı planlamak istersin',
      description: 'Süreli mi yoksa soru sayısı takipli bir çalışma mı planlamak istersin',
      multiple: false,
      options: [
        { label: 'Süreli Çalışma', value: 'time', selected: false, icon: 'icons/question-mark.svg', nextStep: -1 },
        {
          label: 'Soru Sayısı Takipli Çalışma',
          value: 'question',
          selected: false,
          icon: 'icons/question-mark.svg',
          nextStep: -1,
        },
      ],
      actions: [],
    },
    {
      id: 5,
      title: 'Bir günde kaç farklı ders çalışmak istersin?',
      description: 'Bir günde kaç farklı ders çalışmak istersin?',
      multiple: false,
      options: [
        { label: '1', value: '1', selected: false, icon: 'icons/one-svgrepo-com.svg', nextStep: 6 },
        {
          label: '2',
          value: '2',
          selected: false,
          icon: 'icons/two-svgrepo-com.svg',
          nextStep: 6,
        },
        {
          label: '3',
          value: '3',
          selected: false,
          icon: 'icons/three-svgrepo-com.svg',
          nextStep: 6,
        },
      ],
      actions: [],
    },
    {
      id: 6,
      title: 'Ders çalışamayacağın gün var mı?',
      description: 'Ders çalışamayacağın gün var mı?',
      multiple: true,
      options: [
        { label: 'Pazartesi', value: '1', selected: false, icon: 'icons/monday-svgrepo-com.svg', nextStep: 7 },
        { label: 'Salı', value: '2', selected: false, icon: 'icons/tuesday-svgrepo-com.svg', nextStep: 7 },
        { label: 'Çarşamba', value: '3', selected: false, icon: 'icons/wednesday-svgrepo-com.svg', nextStep: 7 },
        { label: 'Perşembe', value: '4', selected: false, icon: 'icons/thursday-svgrepo-com.svg', nextStep: 7 },
        { label: 'Cuma', value: '5', selected: false, icon: 'icons/friday-svgrepo-com.svg', nextStep: 7 },
        { label: 'Cumartesi', value: '6', selected: false, icon: 'icons/saturday-svgrepo-com.svg', nextStep: 7 },
        { label: 'Pazar', value: '7', selected: false, icon: 'icons/sunday-svgrepo-com.svg', nextStep: 7 },
        { label: 'Yok', value: '8', selected: false, icon: 'icons/null-svgrepo-com.svg', nextStep: 7 },
      ],
      actions: [],
    },
    {
      id: 7,
      title: 'Çalışırken zorlandığın ders / dersler hangileri?',
      description: 'Çalışırken zorlandığın ders / dersler hangileri?',
      multiple: true,
      options: [
        { label: 'Hayat Bilgisi', value: '1', selected: false, icon: 'icons/home-svgrepo-com.svg', nextStep: 8 },
        { label: 'Türkçe', value: '2', selected: false, icon: 'icons/alphabet-svgrepo-com.svg', nextStep: 8 },
        { label: 'Matematik', value: '3', selected: false, icon: 'icons/math-svgrepo-com.svg', nextStep: 8 },
        { label: 'Fen Bilimleri', value: '4', selected: false, icon: 'icons/world-svgrepo-com.svg', nextStep: 8 },
        { label: 'Yok', value: '5', selected: false, icon: 'icons/null-svgrepo-com.svg', nextStep: 8 },
      ],
      actions: [],
    },
    {
      id: 8,
      title: 'Artık programını oluşturmaya hazırsın',
      description: 'Artık programını oluşturmaya hazırsın',
      multiple: false,
      options: [],
      actions: [
        {
          label: 'Programı Oluştur',
          value: 'CreateProgram',
        },
        {
          label: 'Vazgeç',
          value: 'Cancel',
        },
      ],
    },
  ];

  next() {
    if (this.currentStep.options.findIndex((f) => f.selected) < 0) {
      this.snackBar.open('Bir seçim yapmadınız', 'Tamam', {
        duration: 2000,
      });
      return;
    }
    const newStepIndex = this.stepIndex() + 1;
    if (newStepIndex < this.stepList.length) {
      this.stepIndex.set(newStepIndex);
    }
    this.userProgramSteps.push(this.currentIndex());
    const nextStep = this.programSteps.findIndex(
      (s) => s.id === this.programSteps[this.currentIndex()].options.find((o) => o.selected)?.nextStep
    );
    if (nextStep !== undefined && nextStep !== -1) {
      this.currentIndex.set(nextStep);
    } else {
      // Eğer bir sonraki adım yoksa, işlemi tamamla veya başka bir şey yap
      console.log('Program oluşturma işlemi tamamlandı.');
    }
  }
  previous() {
    const newStepIndex = this.stepIndex() - 1;
    if (newStepIndex >= 0) {
      this.stepIndex.set(newStepIndex);
    }
    const previousStep = this.userProgramSteps.pop();
    if (previousStep !== undefined) {
      this.currentIndex.set(previousStep);

      console.log('Previous step:', this.programSteps[this.currentIndex()]);
    }
  }

  selectOption(step: any, option: Option) {
    var stepIndex = this.programSteps.findIndex((f) => f.id == step.id);
    if (stepIndex < 0) return;
    if (!this.programSteps[stepIndex].multiple) {
      this.programSteps[stepIndex]?.options.forEach((o) => {
        o.selected = option.value === o.value;
      });
    } else {
      var optIndex = this.programSteps[stepIndex]?.options.findIndex((t) => t.value == option.value);
      if (optIndex > -1) {
        this.programSteps[stepIndex].options[optIndex].selected =
          !this.programSteps[stepIndex].options[optIndex].selected;
      }
    }
  }

  takeAction(actionName: string) {}
}
