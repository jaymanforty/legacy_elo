using System;
using System.Collections.Generic;
using ELO.Models;
using ELO.Entities;
using System.Linq;

namespace ELO.Services
{
    public class Combinations
    {

        private static IEnumerable<int[]> CombinationsRosettaWoRecursion(int m, int n)
        {
            int[] result = new int[m];
            Stack<int> stack = new Stack<int>(m);
            stack.Push(0);
            while (stack.Count > 0)
            {
                int index = stack.Count - 1;
                int value = stack.Pop();
                while (value < n)
                {
                    result[index++] = value++;
                    stack.Push(value);
                    if (index != m) continue;
                    yield return (int[])result.Clone(); // thanks to @xanatos
                                                        //yield return result;
                    break;
                }
            }
        }

        public static IEnumerable<T[]> CombinationsRosettaWoRecursion<T>(T[] array, int m)
        {
            if (array.Length < m)
                throw new ArgumentException("Array length can't be less than number of selected elements");
            if (m < 1)
                throw new ArgumentException("Number of selected elements can't be less than 1");
            T[] result = new T[m];

            foreach (int[] j in CombinationsRosettaWoRecursion(m, array.Length))
            {
                for (int i = 0; i < m; i++)
                {
                    result[i] = array[j[i]];
                }
                yield return result;
            }
        }
    }
}
