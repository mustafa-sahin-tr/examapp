import { CommonModule } from '@angular/common';
import { Component, ElementRef, inject, Input, ViewChild } from '@angular/core';
import { WorksheetCardComponent } from '../worksheet-card/worksheet-card.component';
import { FormControl, FormsModule, ReactiveFormsModule } from '@angular/forms';
import { debounceTime, map, Observable, startWith, switchMap, tap } from 'rxjs';
import { MatFormField, MatLabel } from '@angular/material/form-field';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatInputModule } from '@angular/material/input';
import { TestService } from '../../services/test.service';
import { InstanceSummary, Paged, Test } from '../../models/test-instance';
import { CompletedWorksheetCardComponent } from '../completed-worksheet/completed-worksheet-card.component';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { SectionHeaderComponent } from '../../shared/components/section-header/section-header.component';
import { PaginationComponent } from '../../shared/components/pagination/pagination.component';
import { SubjectService } from '../../services/subject.service';
import { Subject } from '../../models/subject';
import { CustomCheckboxComponent } from '../../shared/components/ms-checkbox/ms-checkbox.component';
import { IsStudentDirective, IsTeacherDirective } from '../../shared/directives/is-student.directive';

@Component({
  selector: 'app-worksheet-list',
  templateUrl: './worksheet-list.component.html',
  styleUrls: ['./worksheet-list.component.scss'],
  standalone: true,
  imports: [CommonModule, WorksheetCardComponent ,MatAutocompleteModule, MatInputModule,
      ReactiveFormsModule, CompletedWorksheetCardComponent, 
      CustomCheckboxComponent, RouterModule,
      SectionHeaderComponent, FormsModule ,PaginationComponent, IsStudentDirective]
})
export class WorksheetListComponent {
  testService = inject(TestService);
  searchControl = new FormControl('');  
  newestWorksheets!: Test[];
  pagedWorksheets!: Paged<Test>;
  
  completedTestWorksheets: InstanceSummary[] = [];

  completedTest$!: Observable<Paged<InstanceSummary>>;
  totalCount = 0;
  pageSize = 10;
  pageNumber = 1;
  route = inject(ActivatedRoute);
  router = inject(Router);
  subjectService = inject(SubjectService);
  selectedSubjectIds: number[] = [];
  selectedGradeId: number | undefined;

    $subjects: Observable<Subject[]> = this.subjectService.loadCategories();

     filteredWorksheets!: Test[];
     @ViewChild('cardContainer', { static: false }) cardContainer!: ElementRef;    

     get totalPages(): number {
      return Math.ceil(this.pagedWorksheets.totalCount / this.pageSize); // Sayfa sayısını hesapla
    }

    

      ngOnInit() {
        this.pagedWorksheets = this.route.snapshot.data['worksheets'] as Paged<Test>;
        this.route.queryParams.subscribe(params => {
          const search = params['search'] ?? '';
          this.searchControl.setValue(search);
          this.makeNewSearch();
        });

        this.completedTest$ = this.testService.getCompleted(1);

        // this.testService.getCompleted(1).subscribe(response => {
        //     this.completedTestWorksheets = response.items;
        // });

        this.testService.getLatest(1).subscribe(response => {
          this.newestWorksheets = response;
        }
        );
      }

      onEnter() {
        this.makeNewSearch();
      }

      makeNewSearch() {
        this.testService.search(this.searchControl.value || '',this.selectedSubjectIds, this.selectedGradeId,1).subscribe(results => {
          this.pagedWorksheets = results;
          this.pageNumber = 1;
        });
      }

      onCardClick(id: number) {
        console.log('Card clicked:', id);
        this.router.navigate(['/test', id]);
      }

      changePage(page: any) {
        console.log('Page changed:', page); 
        this.pageNumber = page;
        this.testService.search(this.searchControl.value || '',this.selectedSubjectIds, this.selectedGradeId, this.pageNumber).subscribe(response => {
          this.pagedWorksheets = response;
        });
      }

      nextPage() {
        console.log('Next page');
        if (this.pageNumber * this.pageSize < this.pagedWorksheets.totalCount) {
          this.pageNumber++;
          this.testService.search(this.searchControl.value || '',this.selectedSubjectIds, this.selectedGradeId, this.pageNumber).subscribe(response => {
            this.pagedWorksheets = response;
          });
        }
      }
    
      prevPage() {
        console.log('Prev page');
        if (this.pageNumber > 1) {
          this.pageNumber--;
          this.testService.search(this.searchControl.value || '',this.selectedSubjectIds, this.selectedGradeId, this.pageNumber).subscribe(response => {
            this.pagedWorksheets = response;
          });
        }
      }

      onAutocompleteSelect(worksheetName: string) {
        this.searchControl.setValue(worksheetName, { emitEvent: false });
        this.onEnter();
      }

      onCheckboxChange(event: { checked: boolean; value: any }) {
        const subjectId = event.value.id;
        if (event.checked) {
          if (!this.selectedSubjectIds.includes(subjectId)) {
            this.selectedSubjectIds.push(subjectId);
            this.makeNewSearch();
          }
        } else {
          const index = this.selectedSubjectIds.indexOf(subjectId);
          if (index > -1) {
            this.selectedSubjectIds.splice(index, 1);
            this.makeNewSearch();
          }
        }
        console.log('Checkbox State:', event.checked, 'Value:', event.value);
      }

      handleLeftNavigation() {
        if (this.cardContainer) {
          const container = this.cardContainer.nativeElement;
          if (container.scrollLeft > 0) {
            container.scrollBy({ left: -600, behavior: 'smooth' });
          } else if (this.pageNumber > 1) {
            // this.prevPage();
          }
        }
      }
    
      handleRightNavigation() {
        if (this.cardContainer) {
          const container = this.cardContainer.nativeElement;
          if (container.scrollLeft + container.clientWidth < container.scrollWidth) {
            container.scrollBy({ left: 600, behavior: 'smooth' });
          } else if (this.pageNumber * this.pageSize < this.pagedWorksheets.totalCount) {            
            // this.nextPage();
          }
        }
      }

      searchQuery = 'dotnet';
      totalResults = 288;
      showRetiredResults = false;
      sortOptions = ['Newest', 'Relevance', 'Popularity'];
      selectedSort = 'Relevance';
      
      filters = {
        category: [
          { name: 'Artificial Intelligence', count: 2, selected: false },
          { name: 'Business', count: 2, selected: false },
          { name: 'Cloud', count: 34, selected: false },
          { name: 'Data', count: 2, selected: false },
          { name: 'IT Ops', count: 19, selected: false },
          { name: 'Security', count: 7, selected: false },
          { name: 'Software Development', count: 230, selected: true }
        ],
        topics: [
          { name: 'ASP.NET Core', count: 57, selected: false },
          { name: 'C#', count: 48, selected: false },
          { name: '.NET', count: 48, selected: false },
          { name: 'Microsoft Azure', count: 29, selected: false }
        ]
      };
      
      courses = [
        {
          title: "What's New in .NET 9",
          author: 'Gill Cleeren',
          date: 'Jan 2025',
          level: 'Intermediate',
          rating: 4.8,
          duration: '1h 58m',
          isNew: true,
          topics: [
            { title: 'Using .NET Aspire in .NET 9', duration: '2m' },
            { title: 'Introducing .NET 9', duration: '15m' },
            { title: 'Understanding .NET Aspire', duration: '8m' }
          ]
        },
        {
          title: '.NET MAUI Fundamentals',
          author: 'Chris Miller',
          date: 'Apr 2024',
          level: 'Beginner',
          rating: 3.8,
          duration: '4h 48m',
          isNew: false,
          topics: [
            { title: 'What Is .NET MAUI', duration: '2m' },
            { title: 'Demo - Running a .NET MAUI App', duration: '4m' },
            { title: 'What Makes up a .NET MAUI Application', duration: '2m' }
          ]
        }
      ];

      toggleRetiredResults() {
        this.showRetiredResults = !this.showRetiredResults;
      }
      

      //  filterWorksheets(value: string) {
      //   const filterValue = value.toLowerCase();
      //   return this.worksheets.filter(worksheet => worksheet.title.toLowerCase().includes(filterValue));
      // }
    
}