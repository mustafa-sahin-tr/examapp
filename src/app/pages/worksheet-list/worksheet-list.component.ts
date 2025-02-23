import { CommonModule } from '@angular/common';
import { Component, ElementRef, inject, Input, ViewChild } from '@angular/core';
import { WorksheetCardComponent } from '../worksheet-card/worksheet-card.component';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { debounceTime, map, Observable, startWith, switchMap, tap } from 'rxjs';
import { MatFormField, MatLabel } from '@angular/material/form-field';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatInputModule } from '@angular/material/input';
import { TestService } from '../../services/test.service';
import { CompletedTest, Paged, Test } from '../../models/test-instance';
import { CompletedWorksheetCardComponent } from '../completed-worksheet/completed-worksheet-card.component';
import { ActivatedRoute, Router } from '@angular/router';
import { SectionHeaderComponent } from '../../shared/components/section-header/section-header.component';

@Component({
  selector: 'app-worksheet-list',
  templateUrl: './worksheet-list.component.html',
  styleUrls: ['./worksheet-list.component.scss'],
  standalone: true,
  imports: [CommonModule, WorksheetCardComponent, MatFormField ,MatAutocompleteModule, MatInputModule,
      MatLabel, ReactiveFormsModule, CompletedWorksheetCardComponent, SectionHeaderComponent]
})
export class WorksheetListComponent {
  testService = inject(TestService);
  searchControl = new FormControl('');  
  
  pagedWorksheets!: Paged<Test>;
  completedTestWorksheets: CompletedTest[] = [];
  totalCount = 0;
  pageSize = 10;
  pageNumber = 1;
  route = inject(ActivatedRoute);
  router = inject(Router);
     filteredWorksheets!: Test[];
     @ViewChild('cardContainer', { static: false }) cardContainer!: ElementRef;    

      ngOnInit() {
        this.pagedWorksheets = this.route.snapshot.data['worksheets'] as Paged<Test>;
        this.searchControl.valueChanges.pipe(
          debounceTime(300),
          switchMap(value => {
            console.log('Search value:', value); // Gelen değeri kontrol et
            return this.testService.search(value || '',1)
          }),
          tap(results => {
            this.pagedWorksheets = results;
            this.pageNumber = 1;            
          })
        ).subscribe();

        this.testService.getCompleted(1).subscribe(response => {
            this.completedTestWorksheets = response.items;
        });
      }

      onEnter() {
        this.testService.search(this.searchControl.value || '',1).subscribe(results => {
          this.pagedWorksheets = results;
          this.pageNumber = 1;
        });
      }

      onCardClick(id: number) {
        this.testService.startTest(id).subscribe(response => {
          this.router.navigate(['/test', id]);
        });   
      }

      nextPage() {
        if (this.pageNumber * this.pageSize < this.totalCount) {
          this.pageNumber++;
          this.testService.search(this.searchControl.value || '', this.pageNumber).subscribe(response => {
            this.pagedWorksheets = response;
          });
        }
      }
    
      prevPage() {
        if (this.pageNumber > 1) {
          this.pageNumber--;
          this.testService.search(this.searchControl.value || '', this.pageNumber).subscribe(response => {
            this.pagedWorksheets = response;
          });
        }
      }

      onAutocompleteSelect(value: string) {
        this.searchControl.setValue(value, { emitEvent: false });
        this.onEnter();
      }

      handleLeftNavigation() {
        if (this.cardContainer) {
          const container = this.cardContainer.nativeElement;
          if (container.scrollLeft > 0) {
            container.scrollBy({ left: -300, behavior: 'smooth' });
          } else if (this.pageNumber > 1) {
            this.pageNumber--;
            this.testService.search(this.searchControl.value || '', this.pageNumber).subscribe(response => {
              this.pagedWorksheets = response;
            });
          }
        }
      }
    
      handleRightNavigation() {
        if (this.cardContainer) {
          const container = this.cardContainer.nativeElement;
          if (container.scrollLeft + container.clientWidth < container.scrollWidth) {
            container.scrollBy({ left: 300, behavior: 'smooth' });
          } else if (this.pageNumber * this.pageSize < this.totalCount) {
            this.pageNumber++;
            this.testService.search(this.searchControl.value || '', this.pageNumber).subscribe(response => {
              this.pagedWorksheets = response;
            });
          }
        }
      }
      

      //  filterWorksheets(value: string) {
      //   const filterValue = value.toLowerCase();
      //   return this.worksheets.filter(worksheet => worksheet.title.toLowerCase().includes(filterValue));
      // }
    
}