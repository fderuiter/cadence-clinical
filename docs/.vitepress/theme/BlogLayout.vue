<script setup>
import DefaultTheme from "vitepress/theme";
import BlogPostHeader from "./components/BlogPostHeader.vue";
import BlogPostFooter from "./components/BlogPostFooter.vue";

const { Layout } = DefaultTheme;
</script>

<template>
  <Layout>
    <template #doc-before>
      <BlogPostHeader />
    </template>
    <template #doc-after>
      <BlogPostFooter />
    </template>
  </Layout>
</template>
