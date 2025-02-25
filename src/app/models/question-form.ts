import { FormArray, FormControl } from "@angular/forms"

export type QuestionForm = {
    text: FormControl<string | null>,
    subText: FormControl<string | null>,
    image: FormControl<string | null>,    
    subjectId: FormControl<number>,
    topicId: FormControl<number>,
    subtopicId: FormControl<number>,
    point: FormControl<number>,
    correctAnswer: FormControl<string>,
    isExample: FormControl<boolean>,
    practiceCorrectAnswer: FormControl<string>,
    answerColCount: FormControl<number>,
    testId:  FormControl<number | null>,
    hasPassage: FormControl<boolean>,
    passageId: FormControl<number | null>,
    passageText: FormControl<string | null>,
    passageImage: FormControl<string | null>,
    passageTitle: FormControl<string | null>,
    answers: FormArray
}