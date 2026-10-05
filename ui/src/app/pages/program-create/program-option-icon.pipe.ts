import { Pipe, PipeTransform } from '@angular/core';
import { resolveProgramOptionIcon } from '../../shared/utils/program-option-icon.util';

/** Seçenek ikonunu güvenli Material Symbols adına çevirir (bkz. resolveProgramOptionIcon). */
@Pipe({ name: 'programOptionIcon' })
export class ProgramOptionIconPipe implements PipeTransform {
  transform(icon: string | null | undefined): string {
    return resolveProgramOptionIcon(icon);
  }
}
