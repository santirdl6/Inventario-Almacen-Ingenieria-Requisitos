import { Component, input } from '@angular/core';

@Component({ selector: 'app-master-data-status', standalone: true, template: '<span>{{ active() ? "Activo" : "Inactivo" }}</span>' })
export class MasterDataStatusComponent { readonly active = input.required<boolean>(); }
