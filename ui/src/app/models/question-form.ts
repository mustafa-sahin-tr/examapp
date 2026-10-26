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
    /** Issue #573: yeni kapsamın türü (0 Okuma metni, 1 Etkinlik). */
    passageKind: FormControl<number>,
    /** Issue #573: yeni etkinlik kapsamının yönergesi. */
    passageInstruction: FormControl<string | null>,
    answers: FormArray
}

export type QuestionCanvasForm = {
    subjectId: FormControl<number>,
    topicId: FormControl<number>,
    subtopicId: FormControl<number>,
    isExample: FormControl<boolean>,
    practiceCorrectAnswer: FormControl<string>,
    testId:  FormControl<number | null>,
    testValue:  FormControl<number | null>,
    bookId:  FormControl<number | null>,
    bookTestId :  FormControl<number | null>,
}