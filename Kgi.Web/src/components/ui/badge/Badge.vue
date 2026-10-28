<script setup lang="ts">
import type { PrimitiveProps } from 'reka-ui';
import type { HTMLAttributes } from 'vue';
import type { BadgeVariants } from '.';
import { cn } from 'cn';
import { Primitive } from 'reka-ui';
import { badgeVariants } from '.';

const props = defineProps<
  PrimitiveProps & {
    variant?: BadgeVariants['variant'];
    class?: HTMLAttributes['class'];
  }
>();
</script>

<template>
  <Primitive
    data-slot="badge"
    :data-variant="variant"
    :class="cn(badgeVariants({ variant }), props.class)"
    :as="as"
    :as-child="asChild"
  >
    <slot />
  </Primitive>
</template>
