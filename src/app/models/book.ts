export interface Book { 
    id: number;
    name: string;
}

export interface BookTest {
    id: number;
    name: string;
    book: Book
}