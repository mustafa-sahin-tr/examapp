export interface Option {
  label: string;
  value: string;
  selected?: boolean;
  icon?: string;
  nextStep?: number;
}

export interface StepAction {
  label: string;
  value: string;
}

export interface ProgramStep {
  id: number;
  title: string;
  description: string;
  options: Option[];
  multiple: boolean;
  actions: StepAction[];
}
