export interface Grade {
  id: number,
  name: string
}


export interface Student {
    id: number;
    userId: number;
    user?: any;  // Eğer gerekirse
    studentNumber: string;
    schoolName: string;
    grade: Grade
  }