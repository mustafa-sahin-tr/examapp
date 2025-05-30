import { NgFor } from '@angular/common';
import { Component, inject, signal, OnInit } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { FormsModule } from '@angular/forms';
import { ProgramStep } from '../../models/programstep';
import { ProgramService } from '../../services/program.service';
import { CreateProgramRequest, UserSelection } from '../../models/program.interfaces';
import { Router } from '@angular/router';
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

export interface UserStepSelection {
  stepId: number;
  stepTitle: string;
  selectedOptions: { label: string; value: string }[];
}

@Component({
  selector: 'app-program-create',
  imports: [NgFor, MatIconModule, MatButtonModule, MatSnackBarModule, MatFormFieldModule, MatInputModule, FormsModule],
  templateUrl: './program-create.component.html',
  styleUrl: './program-create.component.scss',
})
export class ProgramCreateComponent implements OnInit {
  currentIndex = signal(0);
  stepIndex = signal(0);
  isCreatingProgram = signal(false);
  private snackBar = inject(MatSnackBar);
  private programService = inject(ProgramService);
  private router = inject(Router);

  programSteps: ProgramStep[] = [];
  userSelections: UserStepSelection[] = [];
  visitedSteps: Set<number> = new Set();

  // Program creation form fields
  programName = '';
  programDescription = '';

  ngOnInit(): void {
    this.programService.getProgramSteps().subscribe((steps) => {
      this.programSteps = steps;
    });
  }

  get currentStep(): ProgramStep {
    return this.programSteps[this.currentIndex()];
  }

  get isLastStep(): boolean {
    const currentStep = this.currentStep;
    if (!currentStep) return false;

    const selectedOption = currentStep.options.find((o) => o.selected);
    return !selectedOption || !selectedOption.nextStep;
  }

  get totalPossibleSteps(): number {
    // Calculate based on the longest possible path through the steps
    if (this.programSteps.length === 0) return 1;

    // Count unique steps that can be reached
    const reachableSteps = new Set<number>();
    this.calculateReachableSteps(1, reachableSteps); // Assuming first step has ID 1
    return Math.max(reachableSteps.size, this.stepIndex() + 2); // +2 for current and creation step
  }

  private calculateReachableSteps(stepId: number, visited: Set<number>, depth: number = 0): void {
    if (depth > 10 || visited.has(stepId)) return; // Prevent infinite loops

    visited.add(stepId);
    const step = this.programSteps.find((s) => s.id === stepId);
    if (!step) return;

    step.options.forEach((option) => {
      if (option.nextStep) {
        this.calculateReachableSteps(option.nextStep, visited, depth + 1);
      }
    });
  }

  getStepIndicators(): { isCompleted: boolean; isActive: boolean; isUpcoming: boolean }[] {
    const indicators = [];
    const currentStepIndex = this.stepIndex();
    const totalSteps = Math.max(6, currentStepIndex + 2); // Minimum 6 steps or dynamic

    for (let i = 0; i < totalSteps; i++) {
      indicators.push({
        isCompleted: i < currentStepIndex,
        isActive: i === currentStepIndex,
        isUpcoming: i > currentStepIndex,
      });
    }

    return indicators;
  }

  next() {
    const currentStep = this.currentStep;
    if (!currentStep) return;

    const selectedOptions = currentStep.options.filter((o) => o.selected);

    if (selectedOptions.length === 0) {
      this.snackBar.open('Bir seçim yapmadınız', 'Tamam', {
        duration: 2000,
      });
      return;
    }

    // Save current step selection
    this.saveCurrentStepSelection();

    // Check if this is the last step
    if (this.isLastStep) {
      this.isCreatingProgram.set(true);
      return;
    }

    // Find next step
    const selectedOption = selectedOptions[0]; // For single selection, take first one
    const nextStepId = selectedOption.nextStep;

    if (nextStepId) {
      const nextStepIndex = this.programSteps.findIndex((s) => s.id === nextStepId);
      if (nextStepIndex !== -1) {
        this.currentIndex.set(nextStepIndex);
        this.stepIndex.update((i) => i + 1);
      }
    }
  }

  previous() {
    if (this.stepIndex() > 0) {
      // Find the previous step from visited steps or go back in sequence
      this.stepIndex.update((i) => i - 1);

      // If we have a navigation history, use it
      if (this.userSelections.length > 0) {
        const previousSelection = this.userSelections[this.userSelections.length - 1];
        const previousStepIndex = this.programSteps.findIndex((s) => s.id === previousSelection.stepId);
        if (previousStepIndex !== -1) {
          this.currentIndex.set(previousStepIndex);
        }
      }
    }
  }

  saveCurrentStepSelection() {
    const currentStep = this.currentStep;
    if (!currentStep) return;

    const selectedOptions = currentStep.options.filter((o) => o.selected);

    // Remove existing selection for this step if any
    this.userSelections = this.userSelections.filter((s) => s.stepId !== currentStep.id);

    // Add new selection
    this.userSelections.push({
      stepId: currentStep.id,
      stepTitle: currentStep.title,
      selectedOptions: selectedOptions.map((o) => ({ label: o.label, value: o.value })),
    });

    this.visitedSteps.add(currentStep.id);
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

  createProgram() {
    if (!this.programName.trim()) {
      this.snackBar.open('Program adı gereklidir', 'Tamam', {
        duration: 2000,
      });
      return;
    }

    const request: CreateProgramRequest = {
      programName: this.programName.trim(),
      description: this.programDescription.trim(),
      userSelections: this.userSelections.map((selection) => ({
        stepId: selection.stepId,
        selectedValues: selection.selectedOptions.map((o) => o.value),
      })),
    };

    this.programService.createProgram(request).subscribe({
      next: (userProgram) => {
        this.snackBar.open('Program başarıyla oluşturuldu!', 'Tamam', {
          duration: 3000,
        });
        // Reset form or navigate to another page
        this.router.navigate(['/programs']);
      },
      error: (error) => {
        console.error('Program oluşturma hatası:', error);
        this.snackBar.open('Program oluşturulurken bir hata oluştu', 'Tamam', {
          duration: 3000,
        });
      },
    });
  }

  cancelProgramCreation() {
    this.isCreatingProgram.set(false);
    this.programName = '';
    this.programDescription = '';
  }

  resetForm() {
    this.currentIndex.set(0);
    this.stepIndex.set(0);
    this.isCreatingProgram.set(false);
    this.userSelections = [];
    this.visitedSteps.clear();
    this.programName = '';
    this.programDescription = '';

    // Clear all selections
    this.programSteps.forEach((step) => {
      step.options.forEach((option) => {
        option.selected = false;
      });
    });
  }

  get totalSteps(): number {
    return this.userSelections.length + 1; // +1 for current step
  }

  getSelectedOptionsText(selection: UserStepSelection): string {
    return selection.selectedOptions.map((o) => o.label).join(', ');
  }
}
