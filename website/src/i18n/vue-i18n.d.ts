import type { MessageSchema } from "./index"

declare module "vue-i18n" {
  export interface DefineLocaleMessage extends MessageSchema {}
}
