import { toast } from '@premise/ui';
import { useMutation, useQueryClient } from '@tanstack/react-query';

/** The server's error body, when it sent one. */
export function apiError(error: unknown, fallback: string): string {
  const body = (error as { body?: { error?: string } })?.body;
  return body?.error ?? fallback;
}

/**
 * The one feedback contract (UX review P1): every mutation toasts its error
 * with the server's message, optionally toasts success, and invalidates the
 * query keys it dirtied. Pages stop hand-rolling onError/alert/invalidate.
 */
export function useApiMutation<TVariables = void, TData = unknown>(options: {
  mutationFn: (variables: TVariables) => Promise<TData>;
  invalidate?: string[][];
  success?: string;
  errorFallback?: string;
  onSuccess?: (data: TData, variables: TVariables) => void;
  onError?: () => void;
}) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: options.mutationFn,
    onSuccess: (data, variables) => {
      for (const key of options.invalidate ?? [])
        void queryClient.invalidateQueries({ queryKey: key });
      if (options.success) toast.success(options.success);
      options.onSuccess?.(data, variables);
    },
    onError: (error) => {
      toast.error(apiError(error, options.errorFallback ?? 'Request failed'));
      options.onError?.();
    },
  });
}
