import { Component, inject, OnInit, ViewChild, ElementRef } from '@angular/core';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';
import { BookService } from '../../services/book.service';
import { GradesService } from '../../services/grades.service';
import { SubjectService } from '../../services/subject.service';
import { MatSnackBar } from '@angular/material/snack-bar';
import * as XLSX from 'xlsx';
import { TestRow } from '../../../models/test-row';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { FormsModule } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatIconModule } from '@angular/material/icon';
import { ReactiveFormsModule } from '@angular/forms';
import { MatChipsModule } from '@angular/material/chips';
import { MatTableModule } from '@angular/material/table';
import { MatRadioModule } from '@angular/material/radio';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBarModule } from '@angular/material/snack-bar';
import { WorksheetCardComponent } from '../worksheet-card/worksheet-card.component';
import { TestFormComponent } from '../test-form/test-form.component';
import { TestService } from '../../services/test.service';
import { Test } from '../../models/test-instance';
import { ActivatedRoute, NavigationExtras, Router } from '@angular/router';

@Component({
  selector: 'app-test-create-enhanced',
  standalone: true,
  templateUrl: './test-create-enhanced.component.html',
  styleUrls: ['./test-create-enhanced.component.scss'],
  imports: [
    CommonModule,
    MatCardModule,
    MatButtonModule,
    FormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatCheckboxModule,
    MatIconModule,
    ReactiveFormsModule,
    MatChipsModule,
    MatTableModule,
    MatRadioModule,
    MatProgressBarModule,
    MatSnackBarModule,
    WorksheetCardComponent,
    TestFormComponent,
  ],
})
export class TestCreateEnhancedComponent implements OnInit {
  id!: number | null;
  exam!: Test;
  isEditMode: boolean = false;
  testForm!: FormGroup;
  books: any[] = [];
  bookTests: any[] = [];
  grades: any[] = [];
  subjects: any[] = [];
  topics: any[] = [];
  subtopics: any[] = [];
  showAddBookInput = false;
  showAddBookTestInput = false;
  selectedBulkFile: File | null = null;
  isUploading = false;
  bulkImportResults: any = null;
  bulkImportData: TestRow[] = [];
  selectedBulkIndex: number | null = null;
  lastPatchedBulkFormValue: any = null;
  @ViewChild('fileInput') fileInput!: ElementRef<HTMLInputElement>;
  testService = inject(TestService);

  bookService = inject(BookService);
  gradeService = inject(GradesService);
  subjectService = inject(SubjectService);
  snackBar = inject(MatSnackBar);
  fb = inject(FormBuilder);
  route = inject(ActivatedRoute);
  router = inject(Router);

  ngOnInit(): void {
    this.id = this.route.snapshot.paramMap.get('id') ? Number(this.route.snapshot.paramMap.get('id')) : null;
    if (!this.id) {
      const navigation = this.router.getCurrentNavigation();
      this.id = history.state.testValue;
    }
    this.reloadComponent(this.id!);
  }

  public set thisId(value: number | null) {
    this.id = value;
  }

  public reloadComponent(id: number) {
    this.thisId = id;
    this.isEditMode = this.id !== null;
    this.testForm = this.fb.group({
      name: ['', Validators.required],
      description: [''],
      gradeId: ['', Validators.required],
      maxDurationMinutes: [10, [Validators.required, Validators.min(1)]],
      isPracticeTest: [false],
      subtitle: [''],
      imageUrl: [''],
      newBookName: [''],
      newBookTestName: [''],
      bookTestId: [''],
      bookId: [''],
      subjectId: [''],
      topicId: [''],
      subtopicId: [''],
      questionCount: [0],
    });
    this.loadBooks();
    this.loadGrades();
    this.loadSubjects();
    this.loadTest();
    // Form değişikliklerini dinle
    this.testForm.valueChanges.subscribe(() => {
      if (this.selectedBulkIndex !== null && this.bulkImportData[this.selectedBulkIndex]) {
        const current = this.testForm.value;
        const last = this.lastPatchedBulkFormValue;
        const changed = last && Object.keys(current).some((key) => current[key] !== last[key]);
        if (changed) {
          // this.bulkImportData field'ların name alanın tutarken form'da id değerleri var. gradeId, bookId, bookTestId gibi
          // bu yüzden form'da değişiklik yapıldığında bulkImportData'yı güncelle
          // Preserve name fields while updating with form values
          const updatedItem = {
            ...this.bulkImportData[this.selectedBulkIndex],
            name: current.name,
            description: current.description,
            maxDurationMinutes: current.maxDurationMinutes,
            isPracticeTest: current.isPracticeTest,
            subtitle: current.subtitle,
            imageUrl: current.imageUrl,
            subjectId: current.subjectId,
            topicId: current.topicId,
            subtopicId: current.subtopicId,
            __status: 'updated',
          };

          // Update ID fields while maintaining the corresponding name fields
          if (current.gradeId) {
            updatedItem.gradeId = current.gradeId;
            const grade = this.grades.find((g) => g.id === current.gradeId);
            if (grade) updatedItem.gradeName = grade.name;
          }

          if (current.bookId) {
            updatedItem.bookId = current.bookId;
            const book = this.books.find((b) => b.id === current.bookId);
            if (book) updatedItem.bookName = book.name;
          } else if (current.newBookName) {
            updatedItem.bookName = current.newBookName;
          }

          if (current.bookTestId) {
            updatedItem.bookTestId = current.bookTestId;
            const bookTest = this.bookTests.find((bt) => bt.id === current.bookTestId);
            if (bookTest) updatedItem.bookTestName = bookTest.name;
          } else if (current.newBookTestName) {
            updatedItem.bookTestName = current.newBookTestName;
          }

          this.bulkImportData[this.selectedBulkIndex] = updatedItem;
          this.bulkImportData = [...this.bulkImportData]; // tabloyu güncelle
        }
      }
    });
  }

  loadTest() {
    this.testService.get(this.id!).subscribe((exam) => {
      this.bookService.getTestsByBook(exam.bookId || 0).subscribe((data) => {
        this.bookTests = data;
        this.testForm.patchValue({ bookTestId: null, newBookTestName: '' }, { emitEvent: false });

        this.testForm.patchValue({
          name: exam.name,
          description: exam.description,
          gradeId: exam.gradeId,
          maxDurationMinutes: exam.maxDurationSeconds / 60,
          isPracticeTest: exam.isPracticeTest,
          subtitle: exam.subtitle,
          imageUrl: exam.imageUrl,
          bookTestId: exam.bookTestId,
          bookId: exam.bookId,
          questionCount: exam.questionCount,
          subjectId: exam.subjectId || null,
        });

        this.subjectService.getSubjectsByGrade(exam.gradeId!).subscribe((subjects) => {
          this.subjects = subjects;
          this.testForm.patchValue({ subjectId: exam.subjectId || null }, { emitEvent: false });

          if (exam.subjectId && exam.gradeId) {
            this.subjectService.getTopicsBySubjectAndGrade(exam.subjectId, exam.gradeId).subscribe((topics) => {
              this.topics = topics;
              this.testForm.patchValue({ topicId: exam.topicId, subtopicId: null }, { emitEvent: false });

              this.subjectService.getSubTopicsByTopic(exam.topicId || 0).subscribe((subtopics) => {
                this.subtopics = subtopics;
                this.testForm.patchValue({ subtopicId: exam.subtopicId || null }, { emitEvent: false });
              });
            });
          } else {
            this.topics = [];
            this.testForm.patchValue({ topicId: null, subtopicId: null }, { emitEvent: false });
          }
        });
      });
    });
  }

  loadBooks() {
    this.bookService.getAll().subscribe((data) => {
      this.books = data;
    });
  }
  loadGrades() {
    this.gradeService.getGrades().subscribe((data) => {
      this.grades = data;
    });
  }
  loadSubjects(gradeId?: number) {
    if (gradeId) {
      this.subjectService.getSubjectsByGrade(gradeId).subscribe((data) => {
        this.subjects = data;
      });
    } else {
      this.subjectService.loadCategories().subscribe((data) => {
        this.subjects = data;
      });
    }
  }
  onBookChange(bookId: any) {
    this.showAddBookInput = false;
    this.bookService.getTestsByBook(bookId.value).subscribe((data) => {
      this.bookTests = data;
      this.testForm.patchValue({ bookTestId: null, newBookTestName: '' }, { emitEvent: false });
    });
  }
  openNewBookAdd() {
    this.showAddBookInput = true;
    this.testForm.patchValue({ bookId: null }, { emitEvent: false });
    this.bookTests = [];
    this.openNewBookTestAdd();
  }
  onNewBookBlur() {
    const name = this.testForm.get('newBookName')?.value;
    if (!name) {
      this.showAddBookInput = false;
      this.showAddBookTestInput = false;
    }
  }
  openNewBookTestAdd() {
    this.showAddBookTestInput = true;
    this.testForm.patchValue({ bookTestId: null }, { emitEvent: false });
  }
  onSubjectChange(subjectId: any) {
    const gradeId = this.testForm.value.gradeId;
    if (subjectId && gradeId) {
      this.subjectService.getTopicsBySubjectAndGrade(subjectId, gradeId).subscribe((topics) => {
        this.topics = topics;
        this.testForm.patchValue({ topicId: null, subtopicId: null }, { emitEvent: false });
      });
    } else {
      this.topics = [];
      this.testForm.patchValue({ topicId: null, subtopicId: null }, { emitEvent: false });
    }
  }
  onTopicChange(topicId: any) {
    this.subjectService.getSubTopicsByTopic(topicId).subscribe((subtopics) => {
      this.subtopics = subtopics;
      this.testForm.patchValue({ subtopicId: null });
    });
  }

  private createTestPayload(): Test {
    return {
      id: this.id,
      name: this.testForm.value.name,
      description: this.testForm.value.description,
      gradeId: this.testForm.value.gradeId,
      maxDurationSeconds: +this.testForm.value.maxDurationMinutes * 60,
      isPracticeTest: this.testForm.value.isPracticeTest,
      imageUrl: this.testForm.value.imageUrl,
      subtitle: this.testForm.value.subtitle,
      newBookName: this.testForm.value.newBookName,
      newBookTestName: this.testForm.value.newBookTestName,
      bookTestId: !this.testForm.value.bookTestId ? 0 : this.testForm.value.bookTestId,
      bookId: !this.testForm.value.bookId ? 0 : this.testForm.value.bookId,
      subjectId: this.testForm.value.subjectId ? this.testForm.value.subjectId : null,
      topicId: this.testForm.value.topicId ? this.testForm.value.topicId : null,
      subtopicId: this.testForm.value.subtopicId ? this.testForm.value.subtopicId : null,
    };
  }

  onSubmit() {
    // Form valid ise kaydet
    if (this.testForm.valid) {
      // Burada backend'e kaydetme işlemi yapılabilir
      // Örneğin: this.testService.create(this.createTestPayload()).subscribe(...)
      this.snackBar.open('Test kaydedildi!', 'Kapat', { duration: 2000 });
      // İsterseniz formu veya state'i sıfırlayabilirsiniz
    } else {
      this.snackBar.open('Form eksik veya hatalı!', 'Kapat', { duration: 2000 });
    }

    if (this.testForm.valid) {
      this.testService.create(this.createTestPayload()).subscribe((response) => {
        if (this.isEditMode) {
          this.ngOnInit(); // Formu güncelle
        } else {
          this.router.navigate(['/exam', response.examId]);
        }
      });
    }
  }

  public onCreateAsync() {
    var payload = this.createTestPayload();
    payload = { ...payload, id: null }; // id'yi null yaparak yeni bir test oluşturulacağını belirt
    return this.testService.create(payload);
  }

  navigateToTestList() {
    // Örneğin bir router ile test listesine yönlendirme yapılabilir
    // this.router.navigate(['/test-list']);
    this.snackBar.open('Test listesine yönlendiriliyorsunuz...', 'Kapat', { duration: 2000 });
  }
  onBulkFileSelected(event: any) {
    const file = event.target.files[0];
    if (file) {
      this.selectedBulkFile = file;
    }
  }

  async onBulkImport() {
    if (!this.selectedBulkFile) return;
    this.isUploading = true;
    this.bulkImportResults = null;
    try {
      const data = await this.readExcelFile(this.selectedBulkFile);
      // Excel'den gelen veriyi backend DTO formatına çevir
      const exams = data.map((row: any) => {
        const item = {
          name: row['Ad'] || row['Ders'],
          description: row['Açıklama'] || row['Description'] || '',
          gradeId: row['Sınıf'] || row['Grade'] || '',
          maxDurationMinutes: +row['Süre'] || +row['Duration'] || 10,
          isPracticeTest: row['ÇalışmaTesti'] === 'Evet' || row['IsPracticeTest'] === true,
          subtitle: row['AltBaşlık'] || row['Subtitle'] || '',
          badgeText: row['Badge'] || '',
          bookTestId: row['BookTestId'] ? +row['BookTestId'] : undefined,
          bookId: row['BookId'] ? +row['BookId'] : undefined,
          bookName: row['Book'] || '',
          bookTestName: row['BookTest'] || '',
          newBookName: row['YeniKitap'] || '',
          newBookTestName: row['YeniKitapTesti'] || '',
        };
        return {
          ...item,
          __original: { ...item },
        };
      });
      this.bulkImportData = exams;
      this.ensureBulkImportNames(); // Gerekirse ekle
      this.isUploading = false;
    } catch (e) {
      this.bulkImportResults = { success: false, message: 'Excel dosyası okunamadı.' };
      this.isUploading = false;
    }
  }

  private readExcelFile(file: File): Promise<any[]> {
    return new Promise((resolve, reject) => {
      const reader = new FileReader();
      reader.onload = (e: any) => {
        const data = new Uint8Array(e.target.result);
        const workbook = XLSX.read(data, { type: 'array' });
        const sheetName = workbook.SheetNames[0];
        const worksheet = workbook.Sheets[sheetName];
        const json = XLSX.utils.sheet_to_json(worksheet, { defval: '' });
        resolve(json);
      };
      reader.onerror = (err) => reject(err);
      reader.readAsArrayBuffer(file);
    });
  }

  onRowSelect(row: TestRow) {
    // Satır seçildiğinde formu ve state'i güncelle
    const i = this.bulkImportData.indexOf(row);
    if (i === -1) return;
    this.selectedBulkIndex = i;
    // Önceki gradeId'yi al
    const prevGradeId = this.testForm.value.gradeId;
    // Formu seçilen satır ile doldururken valueChanges tetiklenmesin
    this.testForm.patchValue(
      {
        name: row.name,
        description: row.description,
        gradeId: row.gradeId,
        maxDurationMinutes: row.maxDurationMinutes,
        isPracticeTest: row.isPracticeTest,
        subtitle: row.subtitle,
        bookTestId: row.bookTestId,
        bookId: row.bookId,
        newBookName: row.newBookName,
        newBookTestName: row.newBookTestName,
        imageUrl: row.imageUrl,
        subjectId: row.subjectId ? +row.subjectId : null,
        topicId: row.topicId ? +row.topicId : null,
        subtopicId: row.subtopicId ? +row.subtopicId : null,
        questionCount: row.questionCount,
      },
      { emitEvent: false }
    );
    // Kitap ve kitap testi inputlarını göster/gizle
    this.showAddBookInput = !!row.newBookName && !row.bookId;
    this.showAddBookTestInput = !!row.newBookTestName && !row.bookTestId;
    // Son patchlenen değeri sakla
    this.lastPatchedBulkFormValue = { ...this.testForm.value };
    // Eğer gradeId değiştiyse, onGradeChange fonksiyonunu tetikle
    const newGradeId = typeof row.gradeId === 'string' ? parseInt(row.gradeId, 10) : row.gradeId;
    if (newGradeId !== prevGradeId && !!newGradeId) {
      this.onGradeChange(newGradeId);
    }
  }
  // Bulk import tablosu ve form ile ilgili fonksiyonlar
  processBulkImport() {
    // Excel'den gelen tüm satırları backend'e göndermeden önce id'leri normalize et
    const exams = this.bulkImportData.map((item: any) => {
      // GRADE
      let gradeId = item.gradeId;
      if (typeof gradeId === 'string') {
        const found = this.grades.find((g) => g.name === gradeId || g.id === gradeId);
        if (found) gradeId = found.id;
      }
      // BOOK
      let bookId = item.bookId;
      if (typeof bookId === 'string') {
        const found = this.books.find((b) => b.name === bookId || b.id === bookId);
        if (found) bookId = found.id;
      }
      // BOOK TEST
      let bookTestId = item.bookTestId;
      if (typeof bookTestId === 'string' && this.bookTests) {
        const found = this.bookTests.find((bt) => bt.name === bookTestId || bt.id === bookTestId);
        if (found) bookTestId = found.id;
      }
      // SUBJECT
      let subjectId = item.subjectId;
      if (typeof subjectId === 'string' && this.subjects) {
        const found = this.subjects.find((s) => s.name === subjectId || s.id === subjectId);
        if (found) subjectId = found.id;
      }
      // TOPIC
      let topicId = item.topicId;
      if (typeof topicId === 'string' && this.topics) {
        const found = this.topics.find((t) => t.name === topicId || t.id === topicId);
        if (found) topicId = found.id;
      }
      // SUBTOPIC
      let subtopicId = item.subtopicId;
      if (typeof subtopicId === 'string' && this.subtopics) {
        const found = this.subtopics.find((st) => st.name === subtopicId || st.id === subtopicId);
        if (found) subtopicId = found.id;
      }
      return {
        ...item,
        gradeId,
        bookId,
        bookTestId,
        subjectId,
        topicId,
        subtopicId,
        newBookName: item.bookName || '',
        newBookTestName: item.bookTestName || '',
      };
    });

    this.testService.bulkImport({ exams }).subscribe((response) => {
      this.bulkImportResults = {
        success: true,
        message: 'Bulk import işlemi başarılı!',
        data: response,
      };
      this.snackBar.open(this.bulkImportResults.message, 'Kapat', { duration: 3000 });
      this.clearBulkImport();
    });
  }

  hasUpdatedItems() {
    return this.bulkImportData.some((item) => item.__status === 'updated');
  }

  getBulkItemStatus(i: number) {
    if (!this.bulkImportData[i]) return 'pending';
    if (this.selectedBulkIndex === i) {
      if (this.lastPatchedBulkFormValue) {
        const current = this.testForm.value;
        const last = this.lastPatchedBulkFormValue;
        const changed = Object.keys(current).some((key) => current[key] !== last[key]);
        if (changed) return 'updated';
      }
    }
    return this.bulkImportData[i].__status === 'updated' ? 'updated' : 'pending';
  }

  isFieldChanged(field: keyof TestRow, i: number): boolean {
    const item = this.bulkImportData[i];
    if (!item || !item.__original) return false;
    return item[field] !== item.__original[field];
  }

  clearBulkImport() {
    this.bulkImportData = [];
    this.selectedBulkIndex = null;
  }

  // Excel'den gelen veya dışarıdan eklenen her item'a gradeName, bookName, bookTestName ekle
  private ensureBulkImportNames() {
    if (!this.bulkImportData || !this.grades || !this.books) return;
    this.bulkImportData = this.bulkImportData.map((item: any) => {
      // GRADE
      let gradeName = item.gradeName || '';
      if (!gradeName && item.gradeId) {
        const found = this.grades.find((g) => g.id === item.gradeId || g.name === item.gradeId);
        if (found) gradeName = found.name;
      }
      // Excel datasında doğrudan gradeName varsa onu da ata
      if (!gradeName && typeof item['Sınıf'] === 'string') gradeName = item['Sınıf'];
      if (!gradeName && typeof item['Grade'] === 'string') gradeName = item['Grade'];

      // BOOK
      let bookName = item.bookName || '';
      if (!bookName && item.bookId) {
        const foundBook = this.books.find((b) => b.id === item.bookId || b.name === item.bookId);
        if (foundBook) bookName = foundBook.name;
      }
      if (!bookName && item.newBookName) bookName = item.newBookName;
      if (!bookName && typeof item['Book'] === 'string') bookName = item['Book'];

      // BOOK TEST
      let bookTestName = item.bookTestName || '';
      if (!bookTestName && item.bookTestId && this.bookTests) {
        const foundBookTest = this.bookTests.find((bt) => bt.id === item.bookTestId || bt.name === item.bookTestId);
        if (foundBookTest) bookTestName = foundBookTest.name;
      }
      if (!bookTestName && item.newBookTestName) bookTestName = item.newBookTestName;
      if (!bookTestName && typeof item['BookTest'] === 'string') bookTestName = item['BookTest'];

      return { ...item, gradeName, bookName, bookTestName };
    });
  }

  normalizeTrString(str: any): string {
    return (typeof str === 'string' ? str : String(str ?? ''))
      .toLocaleLowerCase('tr-TR')
      .replace(/ı/g, 'i')
      .replace(/İ/g, 'i')
      .replace(/ş/g, 's')
      .replace(/Ş/g, 's')
      .replace(/ğ/g, 'g')
      .replace(/Ğ/g, 'g')
      .replace(/ü/g, 'u')
      .replace(/Ü/g, 'u')
      .replace(/ö/g, 'o')
      .replace(/Ö/g, 'o')
      .replace(/ç/g, 'c')
      .replace(/Ç/g, 'c')
      .trim();
  }

  onBulkItemSelect(i: number, element: any) {
    this.selectedBulkIndex = i;

    let bookId = 0;
    let bookTestId = 0;
    let newBookName = '';
    let newBookTestName = '';
    let gradeId = 0;
    let showAddBookInput = false;

    // Store previous gradeId before patching
    const prevGradeId = this.testForm.value.gradeId;

    // Kitap adı ile eşleşen kitap var mı?
    if (element.bookName) {
      const foundBook = this.books.find(
        (b) => this.normalizeTrString(b.name) === this.normalizeTrString(element.bookName)
      );
      if (foundBook) {
        bookId = foundBook.id;
        newBookName = '';
        this.showAddBookInput = false;
        // Kitap testleri de yüklenmeli
        this.bookService.getTestsByBook(foundBook.id).subscribe((tests) => {
          this.bookTests = tests;
          // Kitap testi adı ile eşleşen test var mı? (bookId varsa ona göre, yoksa genel listede arar)
          if (element.bookTestName) {
            let foundBookTest;
            if (bookId) {
              foundBookTest = this.bookTests.find(
                (bt) => this.normalizeTrString(bt.name) === this.normalizeTrString(element.bookTestName)
              );
            } else {
              foundBookTest = null;
            }
            if (foundBookTest) {
              bookTestId = foundBookTest.id;
              newBookTestName = '';
              this.showAddBookTestInput = false;
              this.testForm.patchValue(
                {
                  bookId: bookId,
                  bookTestId: bookTestId,
                },
                { emitEvent: false }
              );
            } else {
              bookTestId = 0;
              newBookTestName = element.bookTestName;
              this.showAddBookTestInput = true;
              this.testForm.patchValue(
                {
                  bookId: bookId,
                  bookTestId: null,
                  newBookTestName: newBookTestName,
                },
                { emitEvent: false }
              );
            }
          }
        });
      } else {
        bookId = 0;
        newBookName = element.bookName;
        this.showAddBookInput = true;
        this.bookTests = [];
        // Kitap bulunamazsa kitap testi de yeni olarak işaretlenir
        newBookTestName = element.bookTestName;
        this.showAddBookTestInput = true;
        this.testForm.patchValue(
          {
            bookId: null,
            bookTestId: null,
            newBookName: newBookName,
            newBookTestName: newBookTestName,
          },
          { emitEvent: false }
        );
      }
    }

    // bookId ve bookTestId gibi grade id'yi de kontrol et excelde grade'in name alanı geliyor ama form'da id gerekiyor listeden bulsun ve setlesin aynı şekilde
    // case insensitive olarak kontrol et ve türkçe karakterleri normalize et
    if (element.gradeId && typeof element.gradeId === 'string') {
      element.gradeId = element.gradeId.trim();
    }
    if (element.grade && typeof element.grade === 'string') {
      element.grade = element.grade.trim();
    }
    if (element.gradeId) {
      const foundGrade = this.grades.find(
        (g) => this.normalizeTrString(g.name) === this.normalizeTrString(element.gradeId)
      );
      if (foundGrade) {
        gradeId = foundGrade.id;
        this.testForm.patchValue({ gradeId: foundGrade.id }, { emitEvent: false });
      } else {
        gradeId = 0; // Eğer bulunamazsa varsayılan olarak 0
        this.testForm.patchValue({ gradeId: null }, { emitEvent: false });
      }
    }

    // Formu patchle
    this.testForm.patchValue(
      {
        name: element.name,
        description: element.description,
        gradeId: gradeId,
        maxDurationMinutes: element.maxDurationMinutes,
        isPracticeTest: element.isPracticeTest,
        subtitle: element.subtitle,
        // bookTestId: bookTestId,
        // bookId: bookId,
        // newBookName: newBookName,
        // newBookTestName: newBookTestName,
        imageUrl: element.imageUrl,
        subjectId: element.subjectId ? +element.subjectId : null,
        topicId: element.topicId ? +element.topicId : null,
        subtopicId: element.subtopicId ? +element.subtopicId : null,
      },
      { emitEvent: false }
    );

    // Seçildiğinde statü değiştirme! Sadece son patchlenen değeri sakla
    this.lastPatchedBulkFormValue = { ...this.testForm.value };

    // Only trigger onGradeChange if grade actually changed and is valid
    if (gradeId && gradeId !== prevGradeId) {
      this.onGradeChange(gradeId);
    }
  }

  get getSelectedGradeName(): string {
    const selectedGrade = this.grades.find((grade) => grade.id === this.testForm.value.gradeId);
    return selectedGrade ? selectedGrade.name : 'Sınıf Seçin';
  }

  navigateToQuestionCanvas() {
    // Form valid ise soru ekleme adımına geçiş
    if (this.testForm.valid) {
      // Eğer bir router ve testService varsa, aşağıdaki gibi kullanılabilir:
      // this.testService.create(this.createTestPayload()).subscribe(() => {
      //   const navigationExtras = { ... };
      //   this.router.navigate(['/questioncanvas'], navigationExtras);
      // });
      this.snackBar.open('Soru ekleme adımına geçiliyor...', 'Kapat', { duration: 1000 });
      const navigationExtras: NavigationExtras = {
        state: {
          subjectId: null,
          topicId: null,
          subtopicId: null,
          testId: this.testForm.value.subtitle,
          bookId: this.testForm.value.bookId,
          bookTestId: this.testForm.value.bookTestId,
          testValue: this.id,
        },
      };
      setTimeout(() => {
        this.router.navigate(['/questioncanvas'], navigationExtras);
      }, 1000);
    } else {
      this.snackBar.open('Form eksik veya hatalı!', 'Kapat', { duration: 2000 });
    }
  }

  onGradeChange(gradeId: number) {
    // Filter subjects by grade
    this.subjectService.getSubjectsByGrade(gradeId).subscribe((subjects) => {
      this.subjects = subjects;
      // Reset subject, topic, subtopic fields in the form
      this.testForm.patchValue({
        subjectId: null,
        topicId: null,
        subtopicId: null,
      });
      this.topics = [];
      this.subtopics = [];
    });
  }

  // Tek bir satırı orijinal haline döndür
  undoBulkItem(i: number) {
    const item = this.bulkImportData[i];
    if (item && item.__original) {
      // Sadece orijinal veriyi tabloya setle
      this.bulkImportData[i] = { ...item.__original, __original: { ...item.__original }, __status: undefined };
      this.bulkImportData = [...this.bulkImportData]; // tabloyu güncelle
      // Form ve bağlı alanlar için mevcut mantığı tekrar kullan
      this.ensureBulkImportNames();
      this.onBulkItemSelect(i, this.bulkImportData[i]);
    }
  }

  // Tüm satırları orijinal haline döndür
  undoAllBulkItems() {
    this.bulkImportData = this.bulkImportData.map((item) => ({
      ...item.__original,
      __original: { ...item.__original },
    }));
    this.bulkImportData = [...this.bulkImportData]; // tabloyu güncelle
    this.ensureBulkImportNames();
    // Eğer bir satır seçiliyse formu da güncelle
    if (this.selectedBulkIndex !== null && this.bulkImportData[this.selectedBulkIndex]) {
      this.testForm.patchValue({ ...this.bulkImportData[this.selectedBulkIndex].__original }, { emitEvent: false });
      this.lastPatchedBulkFormValue = { ...this.bulkImportData[this.selectedBulkIndex].__original };
    }
  }

  applySubjectToAllBulk(subjectId: any) {
    if (!subjectId) return;
    this.bulkImportData = this.bulkImportData.map((item, i) => {
      const updated = { ...item, subjectId, __status: 'updated' };
      // Optionally reset topic/subtopic if subject changes
      if (item.subjectId !== subjectId) {
        updated.topicId = undefined;
        updated.subtopicId = undefined;
      }
      return updated;
    });
    this.bulkImportData = [...this.bulkImportData];
    this.ensureBulkImportNames();
    // If a row is selected, update the form
    if (this.selectedBulkIndex !== null && this.bulkImportData[this.selectedBulkIndex]) {
      this.testForm.patchValue({ subjectId }, { emitEvent: false });
      this.lastPatchedBulkFormValue = { ...this.testForm.value };
    }
  }

  applyTopicToAllBulk(topicId: any) {
    if (!topicId) return;
    this.bulkImportData = this.bulkImportData.map((item, i) => {
      const updated = { ...item, topicId, __status: 'updated' };
      // Optionally reset subtopic if topic changes
      if (item.topicId !== topicId) {
        updated.subtopicId = undefined;
      }
      return updated;
    });
    this.bulkImportData = [...this.bulkImportData];
    this.ensureBulkImportNames();
    if (this.selectedBulkIndex !== null && this.bulkImportData[this.selectedBulkIndex]) {
      this.testForm.patchValue({ topicId }, { emitEvent: false });
      this.lastPatchedBulkFormValue = { ...this.testForm.value };
    }
  }

  applySubtopicToAllBulk(subtopicId: any) {
    if (!subtopicId) return;
    this.bulkImportData = this.bulkImportData.map((item, i) => ({
      ...item,
      subtopicId,
      __status: 'updated',
    }));
    this.bulkImportData = [...this.bulkImportData];
    this.ensureBulkImportNames();
    if (this.selectedBulkIndex !== null && this.bulkImportData[this.selectedBulkIndex]) {
      this.testForm.patchValue({ subtopicId }, { emitEvent: false });
      this.lastPatchedBulkFormValue = { ...this.testForm.value };
    }
  }

  applyGradeToAllBulk(gradeId: any) {
    if (!gradeId) return;
    const grade = this.grades.find((g) => g.id === gradeId);
    if (!grade) {
      return;
    }
    this.bulkImportData = this.bulkImportData.map((item, i) => {
      const updated = { ...item, gradeId, gradeName: grade.name, __status: 'updated' };
      // Optionally reset subject/topic/subtopic if grade changes
      if (item.gradeId !== gradeId) {
        updated.subjectId = undefined;
        updated.topicId = undefined;
        updated.subtopicId = undefined;
      }
      return updated;
    });
    this.bulkImportData = [...this.bulkImportData];
    this.ensureBulkImportNames();
    // If a row is selected, update the form
    if (this.selectedBulkIndex !== null && this.bulkImportData[this.selectedBulkIndex]) {
      this.testForm.patchValue({ gradeId }, { emitEvent: false });
      this.lastPatchedBulkFormValue = { ...this.testForm.value };
    }
  }
}
