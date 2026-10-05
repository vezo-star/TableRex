<template>
  <div class="fullscreen bg-grey-2 text-center q-pa-md flex flex-center">
    <div>
      <div class="text-h2 text-grey-8">404</div>
      <div class="text-h6 q-mb-lg text-grey-7">This page could not be found.</div>
      <q-btn color="primary" unelevated to="/" label="Go to the dashboard" no-caps />
    </div>
  </div>
</template>

<script setup>
defineOptions({ name: 'ErrorNotFound' })
</script>
