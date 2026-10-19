// The portal's single Pinia instance. It is active from import time, so the
// feature modules and the `state` facade work before (and without) a mounted
// app; `mountPortal` also installs it on the app for devtools and injection.
import { createPinia, setActivePinia } from "pinia";

export const pinia = createPinia();
setActivePinia(pinia);
