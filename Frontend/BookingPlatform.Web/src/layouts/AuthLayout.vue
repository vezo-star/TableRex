<template>
  <q-layout view="hHh lpR fFf">
    <q-page-container>
      <q-page class="tr-auth-page flex flex-center q-pa-md">
        <q-card flat class="auth-card q-pa-lg">
          <div class="row justify-center q-mb-lg">
            <TableRexLogo :size="44" show-tagline />
          </div>
          <router-view />
        </q-card>
      </q-page>
    </q-page-container>
  </q-layout>
</template>

<script setup>
import TableRexLogo from '@/components/brand/TableRexLogo.vue'

defineOptions({ name: 'AuthLayout' })
</script>

<style scoped>
.auth-card {
  width: 100%;
  max-width: 400px;
  border-radius: 22px;
  background: var(--tr-white);
  box-shadow: 0 30px 60px -20px rgba(0, 0, 0, 0.45);
}
</style>
