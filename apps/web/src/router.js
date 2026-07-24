import { createRouter, createWebHistory } from "vue-router";

const routes = [
  {
    path: "/",
    redirect: "/mdr",
  },
  {
    path: "/mdr",
    name: "mdr",
    component: () => import("./views/MdrView.vue"),
  },
  {
    path: "/ecrf",
    name: "ecrf",
    component: () => import("./views/EcrfView.vue"),
  },
  {
    path: "/ctms",
    name: "ctms",
    component: () => import("./views/CtmsView.vue"),
  },
  {
    path: "/audit",
    name: "audit",
    component: () => import("./views/AuditView.vue"),
  },
];

export const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL || "/cadence-clinical/"),
  routes,
});

// Mock OIDC Route guard as per ADR-052
router.beforeEach((to, from, next) => {
  // Let's check requirements
  next();
});
