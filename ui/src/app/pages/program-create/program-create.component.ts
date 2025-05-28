import { NgFor } from '@angular/common';
import { Component, inject, signal, OnInit } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { ProgramStep } from '../../models/programstep';
import { ProgramService } from '../../services/program.service';

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

@Component({
  selector: 'app-program-create',
  imports: [NgFor, MatIconModule, MatButtonModule, MatSnackBarModule],
  templateUrl: './program-create.component.html',
  styleUrl: './program-create.component.scss',
})
export class ProgramCreateComponent implements OnInit {
  currentIndex = signal(0);
  stepList = [0, 1, 2, 3, 4, 5];
  stepIndex = signal(0);
  private snackBar = inject(MatSnackBar);
  private programService = inject(ProgramService);

  programSteps: ProgramStep[] = [];

  ngOnInit(): void {
    this.programService.getProgramSteps().subscribe((steps) => {
      this.programSteps = steps;
    });
  }

  get currentStep(): ProgramStep {
    return this.programSteps[this.currentIndex()];
  }

  userProgramSteps: number[] = [];

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
