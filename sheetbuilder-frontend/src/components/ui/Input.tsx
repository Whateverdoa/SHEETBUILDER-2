import { useId } from 'react'
import type { InputProps } from '../../types'
import { cn } from '../../utils'

export function Input({
  label,
  error,
  placeholder,
  value,
  onChange,
  type = 'text',
  disabled = false,
  required = false,
  className,
  ...props
}: InputProps & React.InputHTMLAttributes<HTMLInputElement>) {
  const id = useId()

  return (
    <div className="space-y-1">
      {label && (
        <label 
          htmlFor={id}
          className="block text-sm font-medium text-slate-700 dark:text-slate-300"
        >
          {label}
          {required && <span className="text-red-500 ml-1">*</span>}
        </label>
      )}
      
      <input
        id={id}
        type={type}
        value={value}
        onChange={(e) => onChange?.(e.target.value)}
        placeholder={placeholder}
        disabled={disabled}
        required={required}
        className={cn(
          'input',
          {
            'border-red-300 dark:border-red-600 focus:ring-red-500': error,
          },
          className
        )}
        {...props}
      />
      
      {error && (
        <p className="text-sm text-red-600 dark:text-red-400">
          {error}
        </p>
      )}
    </div>
  )
}