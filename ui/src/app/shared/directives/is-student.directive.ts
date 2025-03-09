import { Directive, TemplateRef, ViewContainerRef } from '@angular/core';
import { HasRoleDirective } from './has-role.directive';
import { AuthService } from '../../services/auth.service';

@Directive({
  selector: '[appIsStudent]',
})
export class IsStudentDirective extends HasRoleDirective {
  constructor(
    authService: AuthService,
    templateRef: TemplateRef<any>,
    viewContainer: ViewContainerRef
  ) {
    super(authService, templateRef, viewContainer);
    this.checkRole('Student');
  }
}
